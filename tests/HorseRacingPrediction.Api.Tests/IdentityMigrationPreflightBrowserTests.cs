using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Contracts.Horses;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class IdentityMigrationPreflightBrowserTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RequestSettleTimeout = TimeSpan.FromSeconds(35);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout(180_000)]
    public async Task PreflightPage_UsesProgramReadersAndSupportsDesktopMobileKeyboardAndExpiredCursor()
    {
        var repositoryRoot = FindRepositoryRoot();
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "hrp-identity-preflight-browser", Guid.NewGuid().ToString("N"));
        var eventStorePath = Path.Combine(temporaryRoot, "eventstore.db");
        var collectionDirectory = Path.Combine(temporaryRoot, "collection-state");
        var scheduleDirectory = Path.Combine(temporaryRoot, "prediction-state");
        var queuePath = Path.Combine(temporaryRoot, "local-queue.db");
        var protectionDirectory = Path.Combine(temporaryRoot, "data-protection-keys");
        Directory.CreateDirectory(temporaryRoot);
        Directory.CreateDirectory(protectionDirectory);

        var localApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var baseAddress = new Uri($"http://127.0.0.1:{ReserveLoopbackPort()}");
        try
        {
            var cursorLogCount = 0;
            var safeServerExceptionTypes = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.Ordinal);
            var circuitFailureLogCount = 0;
            using var process = StartApiProcess(repositoryRoot, eventStorePath, collectionDirectory,
                scheduleDirectory, queuePath, protectionDirectory, localApiKey, baseAddress,
                line =>
                {
                    if (line is null) return;
                    if (line.Contains("cursor=", StringComparison.OrdinalIgnoreCase))
                        Interlocked.Increment(ref cursorLogCount);
                    if (line.Contains("Unhandled exception in circuit", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("CircuitHost", StringComparison.Ordinal))
                        Interlocked.Increment(ref circuitFailureLogCount);
                    foreach (var exceptionType in new[]
                    {
                        "InvalidOperationException", "SqliteException", "HttpRequestException", "ArgumentException",
                        "NullReferenceException", "JsonException", "OperationCanceledException", "ObjectDisposedException",
                    })
                    {
                        if (line.Contains(exceptionType, StringComparison.Ordinal))
                            safeServerExceptionTypes.AddOrUpdate(exceptionType, 1, static (_, count) => count + 1);
                    }
                });
            try
            {
                await WaitUntilReadyAsync(process, baseAddress);
                var horseId = $"horse-{Guid.NewGuid():D}";
                using (var seedClient = new HttpClient { BaseAddress = baseAddress })
                {
                    seedClient.DefaultRequestHeaders.Add("X-Api-Key", localApiKey);
                    using var seed = await seedClient.PostAsJsonAsync("/api/horses",
                        new RegisterHorseRequest(new($"Preflight fixture {Guid.NewGuid():N}", "preflight-fixture",
                            "牡", new DateOnly(2021, 2, 3), horseId)));
                    Assert.AreEqual(HttpStatusCode.Created, seed.StatusCode,
                        "The browser fixture horse must be seeded only in its unique disposable EventStore.");
                }

                var eventStoreConnectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = eventStorePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false,
                    DefaultTimeout = 1,
                }.ToString();
                await using (var fixtureVerification = new SqliteConnection(eventStoreConnectionString))
                {
                    await fixtureVerification.OpenAsync();
                    await using var fixtureCommand = fixtureVerification.CreateCommand();
                    fixtureCommand.CommandText = "SELECT COUNT(*) FROM Horses WHERE HorseId = $horseId;";
                    fixtureCommand.Parameters.AddWithValue("$horseId", horseId);
                    var seededHorseCount = Convert.ToInt64(await fixtureCommand.ExecuteScalarAsync(),
                        System.Globalization.CultureInfo.InvariantCulture);
                    Assert.AreEqual(1L, seededHorseCount,
                        "The exact configured disposable EventStore must contain the seeded fixture horse.");
                }

                using var playwright = await Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
                var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
                });
                try
                {
                    var page = await browserContext.NewPageAsync();
                    var pageErrorCount = 0;
                    var consoleErrorCount = 0;
                    Task<string>? preflightDocumentBody = null;
                    page.PageError += (_, _) => Interlocked.Increment(ref pageErrorCount);
                    page.Console += (_, message) =>
                    {
                        if (message.Type == "error") Interlocked.Increment(ref consoleErrorCount);
                    };
                    page.Response += (_, response) =>
                    {
                        if (response.Request.ResourceType == "document"
                            && Uri.TryCreate(response.Url, UriKind.Absolute, out var responseUri)
                            && responseUri.AbsolutePath.Equals("/settings/identity-migration-preflight", StringComparison.Ordinal))
                            preflightDocumentBody = response.TextAsync();
                    };
                    await page.GotoAsync(new Uri(baseAddress, "/login?returnUrl=%2Fsettings").ToString(),
                        new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = (float)RequestTimeout.TotalMilliseconds });
                    await page.Locator("input[name='username']").FillAsync("user");
                    await page.Locator("input[name='password']").FillAsync(localApiKey);
                    await page.GetByRole(AriaRole.Button, new() { Name = "ログイン" }).ClickAsync();
                    await page.WaitForURLAsync(new Regex(@"/settings(?:\?.*)?$"),
                        new PageWaitForURLOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });
                    await page.GetByRole(AriaRole.Link, new() { Name = "保存データの移行前調査" }).ClickAsync();
                    await page.WaitForURLAsync(new Regex(@"/settings/identity-migration-preflight(?:\?.*)?$"),
                        new PageWaitForURLOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });
                    var notStarted = page.GetByText("調査はまだ開始されていません");
                    await notStarted.WaitForAsync(new LocatorWaitForOptions
                    { Timeout = (float)RequestTimeout.TotalMilliseconds });
                    Assert.IsTrue(await notStarted.IsVisibleAsync(),
                        "Entering the page must not start a scan implicitly.");
                    Assert.IsNotNull(preflightDocumentBody,
                        "The Settings link must load the preflight page as a full document.");
                    var prerenderedHtml = WebUtility.HtmlDecode(await preflightDocumentBody.WaitAsync(RequestTimeout));
                    Assert.IsTrue(prerenderedHtml.Contains("調査はまだ開始されていません", StringComparison.Ordinal));
                    Assert.IsTrue(HasDisabledPrerenderedStartButton(prerenderedHtml),
                        "The server-rendered Start action must be disabled before its interactive handler is ready.");
                    var startScanButton = page.GetByRole(AriaRole.Button, new() { Name = "新しい調査を開始" });
                    await page.WaitForFunctionAsync("""
                    () => [...document.querySelectorAll('fluent-button,button,[role="button"]')].some(button =>
                        button.textContent?.includes('新しい調査を開始')
                        && button.getAttribute('aria-disabled') !== 'true'
                        && !button.hasAttribute('disabled'))
                    """, null, new PageWaitForFunctionOptions { Timeout = 15_000 });

                    await page.SetViewportSizeAsync(1280, 900);
                    await startScanButton.ClickAsync();
                    try
                    {
                        await page.WaitForFunctionAsync("""
                        () => {
                            const text = document.body.innerText;
                            return text.includes('馬IDの登録情報')
                                || text.includes('このページを読み取れませんでした')
                                || text.includes('続きの読み取りを続けられません');
                        }
                        """, null, new PageWaitForFunctionOptions { Timeout = (float)RequestSettleTimeout.TotalMilliseconds });
                    }
                    catch (TimeoutException)
                    {
                        var timedOutBody = await page.Locator("body").InnerTextAsync();
                        var startButton = page.GetByRole(AriaRole.Button, new() { Name = "新しい調査を開始" });
                        var startButtonCount = await startButton.CountAsync().ConfigureAwait(false);
                        var startEnabled = startButtonCount > 0
                            && await startButton.IsEnabledAsync().ConfigureAwait(false);
                        var nextButtonCount = await page.GetByRole(AriaRole.Button, new() { Name = "次のページを読む" }).CountAsync().ConfigureAwait(false);
                        var retryButtonCount = await page.GetByRole(AriaRole.Button, new() { Name = "現在のページを再読込" }).CountAsync().ConfigureAwait(false);
                        var structure = await page.EvaluateAsync<string>("""
                        () => {
                            const content = document.querySelector('.content-inner');
                            return `h1=${document.querySelectorAll('h1').length};h2=${document.querySelectorAll('h2').length};`
                                + `contentChildren=${content?.children.length ?? -1};contentLength=${content?.textContent?.length ?? -1};`
                                + `intro=${document.querySelectorAll('.preflight-intro').length};result=${document.querySelectorAll('.preflight-result').length};`
                                + `loginInputs=${document.querySelectorAll('input[name="username"],input[name="password"]').length};`
                                + `blazorErrorNode=${document.querySelectorAll('#blazor-error-ui').length}`;
                        }
                        """).ConfigureAwait(false);
                        var timedOutState = $"path={new Uri(page.Url).AbsolutePath};"
                            + $"navLink={timedOutBody.Contains("保存データの移行前調査", StringComparison.Ordinal)};"
                            + $"intro={timedOutBody.Contains("読み取り専用の調査", StringComparison.Ordinal)};"
                            + $"idle={timedOutBody.Contains("調査はまだ開始されていません", StringComparison.Ordinal)};"
                            + $"loading={timedOutBody.Contains("読み取っています", StringComparison.Ordinal)};"
                            + $"result={timedOutBody.Contains("馬IDの登録情報", StringComparison.Ordinal) || timedOutBody.Contains("馬イベント", StringComparison.Ordinal)};"
                            + $"readError={timedOutBody.Contains("このページを読み取れませんでした", StringComparison.Ordinal)};"
                            + $"invalidCursor={timedOutBody.Contains("続きの読み取りを続けられません", StringComparison.Ordinal)};"
                            + $"settingsPage={timedOutBody.Contains("その他設定", StringComparison.Ordinal)};"
                            + $"normalizationSection={timedOutBody.Contains("登録済み名称の正規化", StringComparison.Ordinal)};"
                            + $"repairSection={timedOutBody.Contains("JRA競走馬識別子の不具合修復", StringComparison.Ordinal)};"
                            + $"blazorError={timedOutBody.Contains("An unhandled error has occurred", StringComparison.OrdinalIgnoreCase)};"
                            + $"appBoundary={timedOutBody.Contains("画面を読み込めませんでした", StringComparison.Ordinal)};"
                            + $"startButtonCount={startButtonCount};startEnabled={startEnabled};nextButtonCount={nextButtonCount};retryButtonCount={retryButtonCount};pageErrors={Volatile.Read(ref pageErrorCount)};consoleErrors={Volatile.Read(ref consoleErrorCount)};"
                            + $"structure={structure};"
                            + $"circuitFailureLogs={Volatile.Read(ref circuitFailureLogCount)};serverExceptionTypes={string.Join(',', safeServerExceptionTypes.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"))}";
                        Assert.Fail($"The live preflight request did not settle within {RequestSettleTimeout.TotalSeconds:0}s; safe UI state '{timedOutState}'.");
                    }
                    var firstPageReady = await page.GetByRole(AriaRole.Heading, new() { Name = "馬IDの登録情報" }).IsVisibleAsync();
                    if (!firstPageReady)
                    {
                        var body = await page.Locator("body").InnerTextAsync();
                        var safeState = body.Contains("このページを読み取れませんでした", StringComparison.Ordinal)
                            ? "read-error"
                            : body.Contains("続きの読み取りを続けられません", StringComparison.Ordinal)
                                ? "invalid-cursor"
                                : "request-not-rendered";
                        Assert.Fail($"The live local preflight request reached safe UI state '{safeState}', not a result page.");
                    }
                    var evidenceRegion = page.GetByRole(AriaRole.Region, new() { Name = "馬IDの保存証拠" });
                    await evidenceRegion.GetByRole(AriaRole.Gridcell, new() { Name = horseId }).WaitForAsync(
                        new LocatorWaitForOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });
                    Assert.IsTrue(await page.GetByText("イベント保存先", new() { Exact = true }).CountAsync() > 0);
                    Assert.IsTrue(await page.GetByText("非同時点の観測", new() { Exact = true }).CountAsync() > 0);
                    Assert.IsTrue(await page.GetByText("既知オブジェクト数にはテーブルとインデックスを含みます。テーブル数や保存行数の全件調査ではありません。", new() { Exact = true }).IsVisibleAsync());
                    Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"),
                        "The desktop page must not introduce horizontal document overflow.");

                    await page.ReloadAsync(new PageReloadOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = (float)RequestTimeout.TotalMilliseconds,
                    });
                    var reloadedIdle = page.GetByText("調査はまだ開始されていません", new() { Exact = true });
                    await reloadedIdle.WaitForAsync(new LocatorWaitForOptions
                    { Timeout = (float)RequestTimeout.TotalMilliseconds });
                    Assert.IsTrue(await reloadedIdle.IsVisibleAsync());
                    Assert.AreEqual(0, await page.GetByRole(AriaRole.Heading, new() { Name = "馬IDの登録情報" }).CountAsync(),
                        "A full page reload must discard the prior page and its continuation rather than resume implicitly.");
                    Assert.AreEqual(0, await page.GetByText(horseId, new() { Exact = true }).CountAsync(),
                        "A full page reload must not retain prior evidence.");
                    var restartScanButton = page.GetByRole(AriaRole.Button, new() { Name = "新しい調査を開始" });
                    await page.WaitForFunctionAsync("""
                    () => [...document.querySelectorAll('fluent-button,button,[role="button"]')].some(button =>
                        button.textContent?.includes('新しい調査を開始')
                        && button.getAttribute('aria-disabled') !== 'true'
                        && !button.hasAttribute('disabled'))
                    """, null, new PageWaitForFunctionOptions { Timeout = 15_000 });
                    Assert.AreEqual(0, await page.GetByRole(AriaRole.Heading, new() { Name = "馬IDの登録情報" }).CountAsync(),
                        "The reloaded page must remain idle until the user explicitly starts a fresh scan.");
                    await restartScanButton.ClickAsync();
                    await page.GetByRole(AriaRole.Heading, new() { Name = "馬IDの登録情報" }).WaitForAsync(
                        new LocatorWaitForOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });
                    await evidenceRegion.GetByRole(AriaRole.Gridcell, new() { Name = horseId }).WaitForAsync(
                        new LocatorWaitForOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });

                    var nextButton = page.GetByRole(AriaRole.Button, new() { Name = "次のページを読む" });
                    await nextButton.FocusAsync();
                    await page.Keyboard.PressAsync("Enter");
                    await page.GetByRole(AriaRole.Heading, new() { Name = "馬イベント" }).WaitForAsync(
                        new LocatorWaitForOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });

                    await page.SetViewportSizeAsync(390, 844);
                    var narrowEvidenceCard = page.Locator(".preflight-cards[aria-label='馬IDの保存証拠'] .evidence-card")
                        .Filter(new() { HasText = horseId });
                    await narrowEvidenceCard.WaitForAsync(new LocatorWaitForOptions
                    { State = WaitForSelectorState.Visible, Timeout = (float)RequestTimeout.TotalMilliseconds });
                    Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"),
                        "Loaded evidence must fit the narrow viewport without horizontal document overflow.");
                    await page.SetViewportSizeAsync(1280, 900);

                    await nextButton.FocusAsync();
                    await page.Keyboard.PressAsync("Enter");
                    await page.GetByRole(AriaRole.Heading, new() { Name = "JRAプロフィール" }).WaitForAsync(
                        new LocatorWaitForOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });

                    await using (var drift = new SqliteConnection($"Data Source={eventStorePath};Pooling=False;Default Timeout=1"))
                    {
                        await drift.OpenAsync();
                        await using var driftCommand = drift.CreateCommand();
                        driftCommand.CommandText = "CREATE TABLE preflight_browser_schema_drift (value TEXT NOT NULL);";
                        await driftCommand.ExecuteNonQueryAsync();
                    }

                    await page.GetByRole(AriaRole.Button, new() { Name = "次のページを読む" }).ClickAsync();
                    var invalidCursorTitle = page.GetByText("続きの読み取りを続けられません", new() { Exact = true });
                    try
                    {
                        await invalidCursorTitle.WaitForAsync(
                            new LocatorWaitForOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });
                    }
                    catch (TimeoutException)
                    {
                        var safeSchemaDriftState = await page.EvaluateAsync<string>("""
                        () => {
                            const text = document.body.innerText;
                            const has = value => text.includes(value);
                            return `path=${location.pathname};`
                                + `result=${has('馬IDの登録情報')};eventPhase=${has('馬イベント')};`
                                + `expired=${has('続きの読み取りを続けられません')};`
                                + `readError=${has('このページを読み取れませんでした')};`
                                + `loading=${has('読み取っています')};stale=${has('表示中の結果は前回の観測です')};`
                                + `nextButton=${[...document.querySelectorAll('button')].some(button => button.innerText.includes('次のページを読む'))};`
                                + `restartButton=${[...document.querySelectorAll('button')].some(button => button.innerText.includes('新しい調査を開始'))}`;
                        }
                        """);
                        Assert.Fail($"The schema-drift continuation reached safe UI state '{safeSchemaDriftState}', not the expected expired-cursor state.");
                    }
                    Assert.AreEqual(0, await page.GetByText(horseId, new() { Exact = true }).CountAsync(),
                        "A schema-invalidated cursor must discard stale rows.");
                    Assert.IsTrue(await page.GetByRole(AriaRole.Button, new() { Name = "新しい調査を開始" }).IsVisibleAsync());

                    await page.SetViewportSizeAsync(390, 844);
                    Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"),
                        "The narrow page must fit without horizontal document overflow.");
                    Assert.IsTrue(await invalidCursorTitle.IsVisibleAsync());
                }
                finally
                {
                    await browserContext.CloseAsync();
                }

                Assert.AreEqual(0, Volatile.Read(ref cursorLogCount),
                    "The protected continuation must not appear in host application logs.");
            }
            finally
            {
                await StopOwnedProcessAsync(process);
            }
        }
        finally { DeleteCheckedTemporaryDirectory(temporaryRoot); }
    }

    private static Process StartApiProcess(string repositoryRoot, string eventStorePath,
        string collectionDirectory, string scheduleDirectory, string queuePath,
        string protectionDirectory, string localApiKey, Uri baseAddress, Action<string?> inspectOutput)
    {
        var apiProject = Path.Combine(repositoryRoot, "src", "HorseRacingPrediction.Api", "HorseRacingPrediction.Api.csproj");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "run", "--no-build", "--no-restore", "--no-launch-profile", "--configuration", "Release",
            "--project", apiProject, "--urls", baseAddress.ToString(),
        }) startInfo.ArgumentList.Add(argument);

        var environment = startInfo.Environment;
        environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        environment["DOTNET_ENVIRONMENT"] = "Development";
        environment["ApiKey__Key"] = localApiKey;
        environment["ConnectionStrings__EventStore"] = $"Data Source={eventStorePath};Pooling=False";
        environment["CollectionPlatform__StateDirectory"] = collectionDirectory;
        environment["CollectionPlatform__MaintenanceMode"] = "true";
        environment["CollectionOrchestration__BackgroundSchedulersEnabled"] = "false";
        environment["CollectionJobWatchdog__Enabled"] = "false";
        environment["PredictionScheduling__StateDirectory"] = scheduleDirectory;
        environment["CollectionQueue__Enabled"] = "false";
        environment["CollectionQueue__Provider"] = "Sqs";
        environment["CollectionQueue__LocalDatabasePath"] = queuePath;
        environment["CollectionDispatchTelemetryQueue__Enabled"] = "false";
        environment["CollectionDeadLetterQueueReconciler__Enabled"] = "false";
        environment["DataProtection__KeysDirectory"] = protectionDirectory;
        environment["Logging__LogLevel__Microsoft.AspNetCore"] = "Warning";
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => inspectOutput(args.Data);
        process.ErrorDataReceived += (_, args) => inspectOutput(args.Data);
        if (!process.Start()) throw new InvalidOperationException("The isolated API process did not start.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitUntilReadyAsync(Process process, Uri baseAddress)
    {
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = RequestTimeout };
        using var deadline = new CancellationTokenSource(StartupTimeout);
        while (!deadline.IsCancellationRequested)
        {
            if (process.HasExited)
                Assert.Fail("The isolated API host exited before its local login page became ready.");
            try
            {
                using var response = await client.GetAsync("/login", deadline.Token);
                if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect)
                    return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(200, deadline.Token);
        }
        Assert.Fail("The isolated API login page did not become ready within the bounded startup window.");
    }

    private static bool HasDisabledPrerenderedStartButton(string html)
    {
        foreach (Match match in Regex.Matches(html,
                     @"<(?<tag>fluent-button|button)\b(?<attributes>[^>]*)>(?<content>.*?)</\k<tag>\s*>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            if (!match.Groups["content"].Value.Contains("新しい調査を開始", StringComparison.Ordinal)) continue;
            var attributes = match.Groups["attributes"].Value;
            return Regex.IsMatch(attributes, @"(?:^|\s)disabled(?:\s|=|$)", RegexOptions.IgnoreCase)
                || Regex.IsMatch(attributes, @"aria-disabled\s*=\s*[""']true[""']", RegexOptions.IgnoreCase);
        }
        return false;
    }

    private static async Task StopOwnedProcessAsync(Process process)
    {
        if (process.HasExited) return;
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HorseRacingPrediction.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found for the local browser fixture.");
    }

    private static void DeleteCheckedTemporaryDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var leaf = Path.GetFileName(fullPath);
        var parent = Path.GetFileName(Path.GetDirectoryName(fullPath));
        if (!fullPath.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parent, "hrp-identity-preflight-browser", StringComparison.Ordinal)
            || !Guid.TryParseExact(leaf, "N", out _))
            throw new InvalidOperationException("Refusing to delete a directory outside the test-owned temporary root.");
        Directory.Delete(fullPath, recursive: true);
    }
}
