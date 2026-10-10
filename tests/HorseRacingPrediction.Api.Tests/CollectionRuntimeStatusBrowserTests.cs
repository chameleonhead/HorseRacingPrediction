using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionRuntimeStatusBrowserTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private const string RuntimePath = "/api/v2/admin/collection/operations/runtime-status";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout(180_000)]
    public async Task OperationsPage_RendersDesktopAndMobileWithAuthenticatedRuntimeRefresh()
    {
        var repositoryRoot = FindRepositoryRoot();
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "hrp-runtime-browser", Guid.NewGuid().ToString("N"));
        var apiStateDirectory = Path.Combine(temporaryRoot, "collection-state");
        var dataProtectionDirectory = Path.Combine(temporaryRoot, "data-protection-keys");
        var eventStorePath = Path.Combine(temporaryRoot, "eventstore.db");
        Directory.CreateDirectory(temporaryRoot);
        Directory.CreateDirectory(dataProtectionDirectory);

        var localApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var baseAddress = new Uri($"http://127.0.0.1:{ReserveLoopbackPort()}");
        var runtimeStatusRequestCount = 0;
        var diagnosticLogLock = new object();
        var diagnosticLogTail = new Queue<string>();
        void RememberSanitizedLog(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            var safeLine = line.Replace(localApiKey, "[redacted]", StringComparison.Ordinal);
            lock (diagnosticLogLock)
            {
                diagnosticLogTail.Enqueue(safeLine);
                while (diagnosticLogTail.Count > 40) diagnosticLogTail.Dequeue();
            }
        }
        string GetSanitizedLogTail()
        {
            lock (diagnosticLogLock) return string.Join(Environment.NewLine, diagnosticLogTail);
        }
        Process process;
        try
        {
            process = StartApiProcess(repositoryRoot, apiStateDirectory, eventStorePath,
                dataProtectionDirectory, localApiKey, baseAddress,
                () => Interlocked.Increment(ref runtimeStatusRequestCount), RememberSanitizedLog);
        }
        catch
        {
            DeleteCheckedTemporaryDirectory(temporaryRoot);
            throw;
        }
        using (process)
        {

            try
            {
                await WaitUntilReadyAsync(process, baseAddress, GetSanitizedLogTail);

                using var playwright = await Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                });
                var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
                });
                try
                {
                    var page = await browserContext.NewPageAsync();
                    var loginUrl = new Uri(baseAddress,
                        $"/login?returnUrl={Uri.EscapeDataString("/jobs/operations?tab=failures")}");
                    await page.GotoAsync(loginUrl.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                    await page.Locator("input[name='username']").FillAsync("user");
                    await page.Locator("input[name='password']").FillAsync(localApiKey);
                    await page.GetByRole(AriaRole.Button, new() { Name = "ログイン" }).ClickAsync();
                    await page.WaitForURLAsync(new Regex(@"/jobs/operations(?:\?.*)?$"),
                        new PageWaitForURLOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });

                    await page.GetByRole(AriaRole.Tab, new() { Name = "運用状況" }).WaitForAsync();
                    await page.GetByRole(AriaRole.Tab, new() { NameRegex = new Regex("障害対応") }).WaitForAsync();
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    Assert.AreEqual(0, Volatile.Read(ref runtimeStatusRequestCount),
                        "The runtime endpoint must not be requested while a non-monitoring tab is active.");

                    await page.GetByRole(AriaRole.Tab, new() { Name = "運用状況" }).ClickAsync();
                    await WaitForRuntimeRequestCountAsync(process, () => Volatile.Read(ref runtimeStatusRequestCount), 1);
                    await page.GetByRole(AriaRole.Heading, new() { Name = "バックグラウンド処理の稼働状況" })
                        .WaitForAsync();
                    await WaitForStableRuntimeRequestCountAsync(process, () => Volatile.Read(ref runtimeStatusRequestCount), 1);

                    var runtimeActionNames = new[]
                    {
                    "ジョブ配送", "新規レース探索", "定期再取得", "期限切れ回収",
                    "過去データ再開", "配信失敗の確認", "通知送信", "メトリクス送信",
                };
                    var runtimeActionCards = page.Locator(".runtime-mobile-list [data-runtime-action]");
                    Assert.AreEqual(8, await runtimeActionCards.CountAsync());
                    foreach (var actionName in runtimeActionNames)
                        Assert.IsTrue(await page.GetByText(actionName, new() { Exact = true }).CountAsync() > 0,
                            $"The runtime action '{actionName}' should be visible in the rendered page.");
                    Assert.IsTrue(Regex.IsMatch(await page.Locator("body").InnerTextAsync(),
                        @"更新 \d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2} JST"),
                        "Runtime timestamps should show JST and second-level precision.");

                    await page.SetViewportSizeAsync(1280, 900);
                    Assert.IsTrue(await page.Locator(".runtime-grid-scroll").IsVisibleAsync());
                    Assert.IsFalse(await page.Locator(".runtime-mobile-list").IsVisibleAsync());
                    Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"),
                        "The desktop operations view should not introduce page-level horizontal overflow.");
                    var desktopScreenshot = SaveScreenshotPath("runtime-status-desktop.png");
                    await page.ScreenshotAsync(new PageScreenshotOptions { Path = desktopScreenshot, FullPage = true });
                    TestContext.AddResultFile(desktopScreenshot);

                    var refreshButton = page.GetByRole(AriaRole.Button, new() { Name = "更新" }).First;
                    await refreshButton.FocusAsync();
                    await page.Keyboard.PressAsync("Enter");
                    await WaitForRuntimeRequestCountAsync(process, () => Volatile.Read(ref runtimeStatusRequestCount), 2);
                    await WaitForStableRuntimeRequestCountAsync(process, () => Volatile.Read(ref runtimeStatusRequestCount), 2);

                    await page.GetByRole(AriaRole.Tab, new() { NameRegex = new Regex("障害対応") }).ClickAsync();
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    Assert.AreEqual(2, Volatile.Read(ref runtimeStatusRequestCount),
                        "Leaving monitoring must stop runtime-status reads until monitoring is re-entered.");
                    await page.GetByRole(AriaRole.Tab, new() { Name = "運用状況" }).ClickAsync();
                    await WaitForRuntimeRequestCountAsync(process, () => Volatile.Read(ref runtimeStatusRequestCount), 3);
                    await WaitForStableRuntimeRequestCountAsync(process, () => Volatile.Read(ref runtimeStatusRequestCount), 3);

                    await page.SetViewportSizeAsync(390, 844);
                    await page.WaitForFunctionAsync("""
                    () => {
                        const menu = document.querySelector('.mobile-menu-button');
                        const sidebar = document.querySelector('.sidebar');
                        return menu?.getAttribute('aria-expanded') === 'false'
                            && sidebar !== null
                            && sidebar.getBoundingClientRect().right <= 0.5;
                    }
                    """);
                    Assert.IsTrue(await page.Locator(".runtime-mobile-list").IsVisibleAsync());
                    Assert.IsFalse(await page.Locator(".runtime-grid-scroll").IsVisibleAsync());
                    Assert.AreEqual(8, await runtimeActionCards.CountAsync());
                    Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"),
                        "The mobile operations view should fit without page-level horizontal overflow.");

                    var mobileHeading = page.Locator(".page-header h1");
                    await mobileHeading.ScrollIntoViewIfNeededAsync();
                    Assert.IsTrue(await mobileHeading.EvaluateAsync<bool>("""
                    target => {
                        const bounds = target.getBoundingClientRect();
                        const hit = document.elementFromPoint(bounds.left + 4, bounds.top + 4);
                        return hit !== null && (target.contains(hit) || hit.contains(target));
                    }
                    """), "The mobile navigation must not occlude the page heading.");

                    var firstDetails = page.Locator(".runtime-mobile-list details").First;
                    var firstSummary = firstDetails.Locator("summary");
                    await firstSummary.FocusAsync();
                    await page.Keyboard.PressAsync("Enter");
                    Assert.IsNotNull(await firstDetails.GetAttributeAsync("open"),
                        "The runtime details disclosure should be keyboard operable.");
                    var firstCard = runtimeActionCards.First;
                    await firstCard.ScrollIntoViewIfNeededAsync();
                    Assert.IsTrue(await firstCard.EvaluateAsync<bool>("""
                    target => {
                        const bounds = target.getBoundingClientRect();
                        const hit = document.elementFromPoint(bounds.left + 8, bounds.top + 8);
                        return hit !== null && (target.contains(hit) || hit.contains(target));
                    }
                    """), "The mobile navigation must not occlude the first runtime card.");
                    var detailsLayout = await firstDetails.EvaluateAsync<string>("""
                    details => {
                        const summary = details.querySelector('summary').getBoundingClientRect();
                        const firstDetail = details.querySelector('dl dt').getBoundingClientRect();
                        return JSON.stringify({
                            cardHeight: details.closest('article').getBoundingClientRect().height,
                            gapAfterSummary: firstDetail.top - summary.bottom
                        });
                    }
                    """);
                    TestContext.WriteLine($"Mobile first-card details bounds: {detailsLayout}");
                    var mobileScreenshot = SaveScreenshotPath("runtime-status-mobile.png");
                    await page.ScreenshotAsync(new PageScreenshotOptions { Path = mobileScreenshot, FullPage = true });
                    TestContext.AddResultFile(mobileScreenshot);
                    Assert.AreEqual(3, Volatile.Read(ref runtimeStatusRequestCount));
                }
                finally
                {
                    await browserContext.CloseAsync();
                }
            }
            finally
            {
                try { await StopOwnedProcessAsync(process); }
                finally { DeleteCheckedTemporaryDirectory(temporaryRoot); }
            }
        }
    }

    private static Process StartApiProcess(string repositoryRoot,
        string stateDirectory, string eventStorePath, string dataProtectionDirectory,
        string localApiKey, Uri baseAddress, Action onRuntimeStatusRequest,
        Action<string?> rememberSanitizedLog)
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
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(apiProject);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(baseAddress.ToString());

        var environment = startInfo.Environment;
        environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        environment["DOTNET_ENVIRONMENT"] = "Development";
        environment["ApiKey__Key"] = localApiKey;
        environment["ConnectionStrings__EventStore"] = $"Data Source={eventStorePath};Pooling=False";
        environment["CollectionPlatform__StateDirectory"] = stateDirectory;
        environment["DataProtection__KeysDirectory"] = dataProtectionDirectory;
        environment["CollectionQueue__Enabled"] = "false";
        environment["CollectionOrchestration__BackgroundSchedulersEnabled"] = "false";
        environment["CollectionJobWatchdog__Enabled"] = "false";
        environment["CollectionDeadLetterQueueReconciler__Enabled"] = "false";
        environment["JobFailureNotifications__Enabled"] = "false";
        environment["JobFailureNotifications__TopicArn"] = string.Empty;
        environment["DatabaseMigration__BackupBeforeMigration"] = "false";
        environment["Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information";
        environment["AWS_REGION"] = "ap-northeast-1";
        environment["AWS_EC2_METADATA_DISABLED"] = "true";
        foreach (var name in new[]
        {
            "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN", "AWS_PROFILE",
            "AWS_DEFAULT_PROFILE", "AWS_WEB_IDENTITY_TOKEN_FILE", "AWS_SHARED_CREDENTIALS_FILE",
            "AWS_CONFIG_FILE",
        })
            environment.Remove(name);

        Directory.CreateDirectory(Path.GetDirectoryName(eventStorePath)!);
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The local API process did not start.");
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is { } line && line.Contains("Request finished", StringComparison.Ordinal)
                && line.Contains(RuntimePath, StringComparison.Ordinal))
                onRuntimeStatusRequest();
            rememberSanitizedLog(args.Data);
        };
        process.ErrorDataReceived += (_, args) => rememberSanitizedLog(args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitUntilReadyAsync(Process process, Uri baseAddress, Func<string> getSanitizedLogTail)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < StartupTimeout)
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"The local API exited before readiness (exit code {process.ExitCode}).{Environment.NewLine}{getSanitizedLogTail()}");
            try
            {
                using var response = await client.GetAsync(new Uri(baseAddress, "/login"));
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException(
            $"The isolated local API did not serve its login page within the startup bound.{Environment.NewLine}{getSanitizedLogTail()}");
    }

    private static async Task WaitForRuntimeRequestCountAsync(Process process, Func<int> getRequestCount, int expected)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < RequestTimeout)
        {
            Assert.IsFalse(process.HasExited, "The isolated local API stopped while the browser flow was running.");
            if (getRequestCount() >= expected) return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        Assert.AreEqual(expected, getRequestCount(),
            "The expected runtime-status request was not observed within the bound.");
    }

    private static async Task WaitForStableRuntimeRequestCountAsync(Process process, Func<int> getRequestCount, int expected)
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.AreEqual(expected, getRequestCount(),
            "One UI action should issue only one runtime-status request.");
    }

    private string SaveScreenshotPath(string fileName)
    {
        var resultDirectory = TestContextResultDirectory;
        Directory.CreateDirectory(resultDirectory);
        return Path.Combine(resultDirectory, fileName);
    }

    private string TestContextResultDirectory => TestContext.TestResultsDirectory
        ?? throw new InvalidOperationException("The test result directory is unavailable.");

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "HorseRacingPrediction.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository solution from the test output directory.");
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task StopOwnedProcessAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { throw new TimeoutException("The test-owned local API process did not stop."); }
        }
    }

    private static void DeleteCheckedTemporaryDirectory(string path)
    {
        var normalized = Path.GetFullPath(path);
        var temporaryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hrp-runtime-browser"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to clean a browser-test path outside its dedicated temporary root.");
        if (Directory.Exists(normalized)) Directory.Delete(normalized, recursive: true);
    }
}
