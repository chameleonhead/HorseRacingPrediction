using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationAdminBrowserTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private const string OldHorseId = "horse-d198b3e2-771f-5dcb-94dc-a739e98b6119";
    private const string AddedHorseId = "horse-8dba8ca7-bbc4-4e19-8cd0-2028e12f0085";
    private const string AddedJraNumber = "2024104541";

    [TestMethod]
    [Timeout(180_000)]
    public async Task AdminPageUsesProgramStoresStagesAndSupportsMobileKeyboardReconnect()
    {
        await using var stores = await HorseIdentityMigrationStoreEngineTests.StoreFixture.CreateAsync();
        var repositoryRoot = FindRepositoryRoot();
        var protectionDirectory = Path.Combine(stores.Root, "data-protection-keys");
        Directory.CreateDirectory(protectionDirectory);
        var screenshotDirectory = Path.Combine(Path.GetTempPath(), "hrp-horse-identity-ui-screenshots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(screenshotDirectory);
        await stores.SeedHorseAsync(OldHorseId);
        await stores.SeedSerializedHorseEventAsync(OldHorseId);
        await stores.SeedHorseAsync(AddedHorseId);
        await stores.SeedSerializedHorseEventAsync(AddedHorseId);
        var localApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var baseAddress = new Uri($"http://127.0.0.1:{ReserveLoopbackPort()}");
        var endpointLog = new ConcurrentQueue<string>();
        using var process = StartApiProcess(repositoryRoot, stores, protectionDirectory, localApiKey, baseAddress, endpointLog);
        try
        {
            await WaitUntilReadyAsync(process, baseAddress);
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            });
            try
            {
                var page = await browserContext.NewPageAsync();
                Task<string>? migrationDocumentBody = null;
                page.Response += (_, response) =>
                {
                    if (response.Request.ResourceType == "document"
                        && Uri.TryCreate(response.Url, UriKind.Absolute, out var uri)
                        && uri.AbsolutePath.Equals("/settings/horse-id-migration", StringComparison.Ordinal))
                        migrationDocumentBody = response.TextAsync();
                };

                await page.GotoAsync(new Uri(baseAddress, "/login?returnUrl=%2Fsettings").ToString(),
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = (float)RequestTimeout.TotalMilliseconds });
                await page.Locator("input[name='username']").FillAsync("user");
                await page.Locator("input[name='password']").FillAsync(localApiKey);
                await page.GetByRole(AriaRole.Button, new() { Name = "ログイン" }).ClickAsync();
                await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/settings(?:\?.*)?$"),
                    new PageWaitForURLOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });
                await page.GetByRole(AriaRole.Link, new() { Name = "馬ID移行の仮説確認と実行依頼" }).ClickAsync();
                await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/settings/horse-id-migration(?:\?.*)?$"),
                    new PageWaitForURLOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });
                await page.GetByText("名前だけでは同一性を確認できません").WaitForAsync(new LocatorWaitForOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                Assert.IsNotNull(migrationDocumentBody, "The approved Settings link must load the normal page document.");
                var prerenderedHtml = WebUtility.HtmlDecode(await migrationDocumentBody.WaitAsync(RequestTimeout));
                Assert.IsTrue(HasDisabledPrerenderedHorseSearchButton(prerenderedHtml),
                    "The generic search action must remain disabled until its interactive handler is ready.");
                Assert.IsFalse(Regex.IsMatch(prerenderedHtml, @"<fluent-checkbox\b", RegexOptions.IgnoreCase),
                    "The default request must be empty; no candidate selection is present before the operator adds one.");
                Assert.IsTrue(await page.GetByText("候補はまだありません。", new() { Exact = false }).IsVisibleAsync());

                await page.GetByRole(AriaRole.Button, new() { Name = "保存先を診断" }).ClickAsync();
                await page.GetByText("診断の読み取りは完了しました", new() { Exact = false }).WaitForAsync(new LocatorWaitForOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                Assert.IsTrue(await page.GetByText("候補を選ばずに保存先の構造だけを確認できます。", new() { Exact = false }).IsVisibleAsync());
                Assert.IsTrue(await page.GetByRole(AriaRole.Button, new() { Name = "安全な診断JSONをコピー" }).IsVisibleAsync());

                var searchButton = page.GetByRole(AriaRole.Button, new() { Name = "登録馬を検索" });
                await Assertions.Expect(searchButton).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                await page.GetByLabel("登録馬名またはIDで検索").FillAsync(AddedHorseId);
                await searchButton.ClickAsync();
                var horseSelector = page.GetByRole(AriaRole.Combobox, new() { Name = "追加する旧ID" });
                await horseSelector.FocusAsync();
                Assert.IsTrue(await horseSelector.EvaluateAsync<bool>("element => document.activeElement === element || (element.shadowRoot !== null && element.shadowRoot.activeElement !== null)"),
                    "Keyboard focus must reach the existing-horse selector.");
                await horseSelector.PressAsync("ArrowDown");
                await horseSelector.PressAsync("Enter");
                await Assertions.Expect(horseSelector).ToHaveAttributeAsync("current-value", AddedHorseId,
                    new LocatorAssertionsToHaveAttributeOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });
                Assert.AreEqual(AddedHorseId, await horseSelector.GetAttributeAsync("current-value"),
                    "Keyboard selection must choose the exact registered HorseId returned by the real search endpoint.");
                var jraInput = page.GetByLabel("JRA馬番号（半角数字10桁）");
                await jraInput.FocusAsync();
                await jraInput.PressSequentiallyAsync(AddedJraNumber);
                Assert.AreEqual(AddedJraNumber, await jraInput.InputValueAsync(),
                    "The JRA number should be entered through the focused textbox keyboard path.");
                await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "候補へ追加" }))
                    .ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions
                    { Timeout = (float)RequestTimeout.TotalMilliseconds });
                await page.GetByRole(AriaRole.Button, new() { Name = "候補へ追加" }).ClickAsync();

                var candidateCheckbox = page.GetByRole(AriaRole.Checkbox,
                    new() { Name = $"Fixture Horse {AddedHorseId} を移行対象として選択" });
                await candidateCheckbox.FocusAsync();
                Assert.IsTrue(await candidateCheckbox.EvaluateAsync<bool>("element => document.activeElement === element || (element.shadowRoot !== null && element.shadowRoot.activeElement !== null)"),
                    "Keyboard focus must reach the candidate selector before the Space key is sent.");
                await candidateCheckbox.PressAsync("Space");
                await Assertions.Expect(candidateCheckbox).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                var acceptHypothesis = page.GetByRole(AriaRole.Checkbox,
                    new() { Name = $"Fixture Horse jra-horse:{AddedJraNumber} の仮説を確認" });
                await acceptHypothesis.FocusAsync();
                await acceptHypothesis.PressAsync("Space");
                await Assertions.Expect(acceptHypothesis).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                var reviewButton = page.GetByRole(AriaRole.Button, new() { Name = "選択内容を確認" });
                await Assertions.Expect(reviewButton).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                await reviewButton.ClickAsync();
                await page.GetByText("選択した登録ルートの確認").WaitForAsync(new LocatorWaitForOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                Assert.IsTrue(await page.GetByText("全データの検証は未実施です", new() { Exact = false }).IsVisibleAsync());
                var candidateGrid = page.Locator(".migration-candidates .migration-grid");
                Assert.IsTrue(await candidateGrid.GetByText(AddedHorseId, new() { Exact = true }).IsVisibleAsync());
                Assert.IsTrue(await candidateGrid.GetByText($"Fixture Horse → jra-horse:{AddedJraNumber}", new() { Exact = true }).IsVisibleAsync());
                await page.Locator(".migration-candidates").ScrollIntoViewIfNeededAsync();
                await page.ScreenshotAsync(new PageScreenshotOptions
                { Path = Path.Combine(screenshotDirectory, "desktop-preview.png") });

                await page.GetByRole(AriaRole.Button, new() { Name = "内容を確認して依頼を保存" }).ClickAsync();
                var confirmSaveButton = page.GetByRole(AriaRole.Button, new() { Name = "全件検証待ちとして保存" });
                await Assertions.Expect(confirmSaveButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                await confirmSaveButton.ClickAsync();
                await page.GetByText("実行開始前の依頼です").WaitForAsync(new LocatorWaitForOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                Assert.IsTrue(await page.GetByText("実行開始前の依頼です").IsVisibleAsync(),
                    "Saving an operation must display a receipt without starting the workflow.");
                Assert.AreEqual(1, CountEndpoint(endpointLog, "POST", "/api/v2/admin/horse-id-migration-operations"),
                    $"The server-side operation stage must be observed through the isolated host's real endpoint logs. Related output: {string.Join(" | ", endpointLog.Where(line => line.Contains("horse-id-migration", StringComparison.OrdinalIgnoreCase)))}");
                Assert.IsTrue(page.Url.Contains("/settings/horse-id-migration?operationId=", StringComparison.Ordinal),
                    "The saved GUID should be addressable through the same Settings URL without fetching status automatically.");
                await page.SetViewportSizeAsync(390, 844);
                Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"),
                    "The mobile saved-operation screen must not overflow horizontally.");
                Assert.IsTrue(await page.Locator(".migration-candidates .migration-cards").IsVisibleAsync());
                Assert.IsFalse(await page.Locator(".migration-grid").First.IsVisibleAsync());
                await Assertions.Expect(page.Locator(".migration-candidates .migration-cards").GetByText(AddedHorseId, new() { Exact = true }))
                    .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });
                await AssertTextFitsItsMobileCellAsync(
                    page.Locator(".migration-candidates .migration-cards").GetByText(AddedHorseId, new() { Exact = true }));
                var pendingReviewFingerprints = await page.Locator(".migration-preview").EvaluateAllAsync<int>("""
                    roots => roots.flatMap(root => Array.from(root.querySelectorAll('*')))
                        .filter(element => /^[A-Fa-f0-9]{64}$/.test(element.textContent.trim())).length
                    """);
                Assert.AreEqual(0, pendingReviewFingerprints,
                    "The root-only pending review must not invent or display a full-manifest fingerprint.");
                await page.Locator(".migration-candidates").ScrollIntoViewIfNeededAsync();
                await page.ScreenshotAsync(new PageScreenshotOptions
                { Path = Path.Combine(screenshotDirectory, "mobile-preview.png") });
                await page.SetViewportSizeAsync(1280, 900);
                var statusReadsBeforeReload = CountStatusReads(endpointLog);
                await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = (float)RequestTimeout.TotalMilliseconds });
                await page.GetByRole(AriaRole.Button, new() { Name = "状態を更新" }).WaitForAsync(new LocatorWaitForOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                Assert.AreEqual(statusReadsBeforeReload, CountStatusReads(endpointLog),
                    "Reload must only restore the operation ID; the real API boundary must see no status lookup.");
                Assert.AreEqual(0, CountEndpoint(endpointLog, "POST", "/api/v2/admin/horse-id-migration-operations/{operationId}/execute"),
                    "Reloading a saved operation ID must never dispatch execution to the real API.");
                await page.SetViewportSizeAsync(390, 844);
                Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"),
                    "The mobile status/reconnect screen must not overflow horizontally.");
                Assert.IsTrue(await page.GetByText("候補はまだありません。", new() { Exact = false }).IsVisibleAsync(),
                    "Reload must not restore presets or auto-fetch the saved manifest as editable candidates.");
                Assert.IsFalse(await page.Locator(".migration-grid").First.IsVisibleAsync());

                await page.GetByRole(AriaRole.Button, new() { Name = "状態を更新" }).ClickAsync();
                await page.GetByText("手動操作待ち").WaitForAsync(new LocatorWaitForOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                Assert.AreEqual(statusReadsBeforeReload + 1, CountStatusReads(endpointLog),
                    "The explicit status button is a positive control for observing real server-side API reads.");
                Assert.IsTrue(await page.GetByRole(AriaRole.Button, new() { Name = "この保存済み依頼を実行" }).IsVisibleAsync(),
                    "Execution must become available only after an explicit status read restores the saved receipt.");
                Assert.AreEqual(0, CountEndpoint(endpointLog, "POST", "/api/v2/admin/horse-id-migration-operations/{operationId}/execute"));
                var savedRequestHorse = page.Locator(".migration-saved-request").GetByText(AddedHorseId, new() { Exact = true });
                await Assertions.Expect(savedRequestHorse).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                await AssertTextFitsItsMobileCellAsync(savedRequestHorse);
                await page.GetByText("手動操作待ち").ScrollIntoViewIfNeededAsync();
                await page.ScreenshotAsync(new PageScreenshotOptions
                { Path = Path.Combine(screenshotDirectory, "mobile-receipt-status.png") });
                Console.WriteLine($"T17 sanitized UI screenshots saved to: {screenshotDirectory}");
            }
            finally
            {
                await browserContext.CloseAsync();
            }
        }
        finally
        {
            await StopOwnedProcessAsync(process);
        }
    }

    private static Process StartApiProcess(string repositoryRoot,
        HorseIdentityMigrationStoreEngineTests.StoreFixture stores,
        string protectionDirectory, string localApiKey, Uri baseAddress,
        ConcurrentQueue<string> endpointLog)
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
        environment["ConnectionStrings__EventStore"] = $"Data Source={stores.EventStorePath};Pooling=False";
        environment["CollectionPlatform__StateDirectory"] = Path.GetDirectoryName(stores.CollectionPath)!;
        environment["CollectionPlatform__DatabaseFileName"] = Path.GetFileName(stores.CollectionPath);
        environment["CollectionPlatform__MaintenanceMode"] = "true";
        environment["CollectionOrchestration__BackgroundSchedulersEnabled"] = "false";
        environment["CollectionJobWatchdog__Enabled"] = "false";
        environment["PredictionScheduling__StateDirectory"] = Path.GetDirectoryName(stores.SchedulePath)!;
        environment["PredictionScheduling__StoreFileName"] = Path.GetFileName(stores.SchedulePath);
        environment["CollectionQueue__Enabled"] = "false";
        environment["CollectionQueue__Provider"] = "Sqs";
        environment["CollectionQueue__LocalDatabasePath"] = stores.QueuePath;
        environment["CollectionDispatchTelemetryQueue__Enabled"] = "false";
        environment["CollectionDeadLetterQueueReconciler__Enabled"] = "false";
        environment["DataProtection__KeysDirectory"] = protectionDirectory;
        environment["Logging__LogLevel__Microsoft.AspNetCore"] = "Warning";
        environment["Logging__LogLevel__Microsoft.AspNetCore.Routing.EndpointMiddleware"] = "Information";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null) endpointLog.Enqueue(eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null) endpointLog.Enqueue(eventArgs.Data);
        };
        if (!process.Start()) throw new InvalidOperationException("The isolated API process did not start.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static int CountStatusReads(ConcurrentQueue<string> endpointLog) =>
        endpointLog.Count(line => line.Contains("Executing endpoint 'HTTP: GET /api/v2/admin/horse-id-migration-operations/{operationId}", StringComparison.Ordinal)
            || line.Contains("Executing endpoint 'HTTP: GET /api/v2/admin/horse-id-migration-requests/{requestKey}", StringComparison.Ordinal));

    private static int CountEndpoint(ConcurrentQueue<string> endpointLog, string method, string route) =>
        endpointLog.Count(line => line.Contains($"Executing endpoint 'HTTP: {method} {route}", StringComparison.Ordinal));

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
                if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(200, deadline.Token);
        }
        Assert.Fail("The isolated API login page did not become ready within the bounded startup window.");
    }

    private static async Task StopOwnedProcessAsync(Process process)
    {
        if (process.HasExited) return;
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static bool HasDisabledPrerenderedHorseSearchButton(string html)
    {
        foreach (Match match in Regex.Matches(html,
                     @"<(?<tag>fluent-button|button)\b(?<attributes>[^>]*)>(?<content>.*?)</\k<tag>\s*>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var attributes = match.Groups["attributes"].Value;
            if (!match.Groups["content"].Value.Contains("登録馬を検索", StringComparison.Ordinal)) continue;
            return Regex.IsMatch(attributes, @"(?:^|\s)disabled(?:\s|=|$)", RegexOptions.IgnoreCase)
                || Regex.IsMatch(attributes, "aria-disabled\\s*=\\s*[\\\"']true[\\\"']", RegexOptions.IgnoreCase);
        }
        return false;
    }

    private static async Task AssertGridCellsAreFullyVisibleAsync(ILocator grid)
    {
        Assert.IsGreaterThan(0, await grid.Locator("td").CountAsync(),
            "The Fluent grid should render data cells for visual inspection.");
        var failures = await grid.Locator("td").EvaluateAllAsync<string[]>("""
            cells => cells.flatMap(cell => {
                const content = cell.querySelector('.multiline-text') || cell;
                const style = getComputedStyle(content);
                const rect = content.getBoundingClientRect();
                const clipped = rect.width === 0 || rect.height === 0
                    || style.whiteSpace === 'nowrap'
                    || content.scrollWidth > content.clientWidth + 1
                    || content.scrollHeight > content.clientHeight + 1;
                return clipped ? [`${cell.innerText.trim()}: ${style.whiteSpace} ${content.scrollWidth}x${content.scrollHeight} > ${content.clientWidth}x${content.clientHeight}`] : [];
            })
            """);

        Assert.AreEqual(0, failures.Length,
            $"Critical Fluent grid text must wrap and remain fully visible; clipped cells: {string.Join("; ", failures)}");
    }

    private static async Task AssertTextFitsItsMobileCellAsync(ILocator text)
    {
        var fullyRendered = await text.EvaluateAsync<bool>("""
            element => {
                const cell = element.closest('dd') ?? element.parentElement;
                const rect = cell.getBoundingClientRect();
                const style = getComputedStyle(cell);
                return rect.width > 0 && rect.height > 0 && style.whiteSpace !== 'nowrap'
                    && cell.scrollWidth <= cell.clientWidth + 1 && cell.scrollHeight <= cell.clientHeight + 1;
            }
            """);
        Assert.IsTrue(fullyRendered,
            "The full HorseId must be visibly wrapped within its 390px card without clipping.");
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
}
