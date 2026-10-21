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
    private const int MaximumRecordedResponseStatuses = 16;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void BrowserFailureDiagnostics_OnlyEmitBoundedAllowlistedValues()
    {
        const string canary = "synthetic-browser-diagnostic-canary";
        var diagnostics = new SanitizedBrowserDiagnostics();
        diagnostics.ObserveNavigation("http://localhost/jobs/operations?tab=failures");
        diagnostics.ObserveConsole("error", $"{canary}; header={canary}; body={canary}");
        diagnostics.ObservePageError($"{canary}; header={canary}; body={canary}");
        diagnostics.ObserveRequest($"https://example.invalid/{canary}");
        diagnostics.ObserveRequestFailure($"https://example.invalid/{canary}?token={canary}");
        for (var index = 0; index < MaximumRecordedResponseStatuses + 5; index++)
            diagnostics.ObserveResponse($"https://example.invalid/{canary}?body={canary}", 503);

        var snapshot = diagnostics.ToSafeSnapshot();

        Assert.IsFalse(snapshot.Contains(canary, StringComparison.Ordinal));
        Assert.IsTrue(snapshot.Contains("path=Operations", StringComparison.Ordinal));
        Assert.IsTrue(snapshot.Contains("tab=Failures", StringComparison.Ordinal));
        Assert.IsTrue(snapshot.Contains("requestFailedOtherCount=1", StringComparison.Ordinal));
        Assert.IsTrue(snapshot.Contains("blazorTransportRequestCount=0", StringComparison.Ordinal));
        Assert.IsTrue(snapshot.Contains("responseStatusesRecorded=16", StringComparison.Ordinal));
        Assert.IsTrue(snapshot.Contains("responseStatusesOmitted=5", StringComparison.Ordinal));
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task MonitoringNavigationWait_UsesCommitButStillRequiresExactUrlAndRenderedContent()
    {
        const string monitoringPattern = @"/jobs/operations\?tab=monitoring$";
        const int SyntheticWaitTimeoutMilliseconds = 2_000;
        var baseAddress = new Uri($"http://127.0.0.1:{ReserveLoopbackPort()}");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
        await using var browserContext = await browser.NewContextAsync();

        async Task<(IPage Page, TaskCompletionSource ReleaseHeldLoad)> CreateSyntheticPageAsync()
        {
            var page = await browserContext.NewPageAsync();
            var releaseHeldLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await page.RouteAsync("**/*", async route =>
            {
                try
                {
                    var requestUri = new Uri(route.Request.Url);
                    if (requestUri.AbsolutePath.Equals("/held.png", StringComparison.Ordinal))
                    {
                        await releaseHeldLoad.Task;
                        await route.FulfillAsync(new RouteFulfillOptions
                        {
                            Status = 200,
                            ContentType = "image/png",
                            Body = "held-load-released",
                        });
                        return;
                    }

                    if (requestUri.AbsolutePath.Equals("/jobs/operations", StringComparison.Ordinal))
                    {
                        await route.FulfillAsync(new RouteFulfillOptions
                        {
                            Status = 200,
                            ContentType = "text/html; charset=utf-8",
                            Body = """
                                <!doctype html>
                                <html><body>
                                <h1>バックグラウンド処理の稼働状況</h1>
                                <img src="/held.png" alt="held load">
                                </body></html>
                                """,
                        });
                        return;
                    }

                    await route.AbortAsync();
                }
                catch (PlaywrightException)
                {
                    // Closing a scenario page cancels its intentionally held resource.
                }
            });
            return (page, releaseHeldLoad);
        }

        var (commitPage, commitRelease) = await CreateSyntheticPageAsync();
        try
        {
            var commitWait = commitPage.WaitForURLAsync(new Regex(monitoringPattern),
                new PageWaitForURLOptions
                {
                    Timeout = SyntheticWaitTimeoutMilliseconds,
                    WaitUntil = WaitUntilState.Commit,
                });
            await commitPage.GotoAsync(new Uri(baseAddress, "/jobs/operations?tab=monitoring").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = SyntheticWaitTimeoutMilliseconds });
            await commitWait;
            await commitPage.GetByRole(AriaRole.Heading,
                    new() { Name = "バックグラウンド処理の稼働状況" })
                .WaitForAsync(new LocatorWaitForOptions { Timeout = SyntheticWaitTimeoutMilliseconds });
        }
        finally
        {
            commitRelease.TrySetResult();
            await commitPage.CloseAsync();
        }

        var (loadPage, loadRelease) = await CreateSyntheticPageAsync();
        try
        {
            var loadWait = loadPage.WaitForURLAsync(new Regex(monitoringPattern),
                new PageWaitForURLOptions { Timeout = SyntheticWaitTimeoutMilliseconds });
            await loadPage.GotoAsync(new Uri(baseAddress, "/jobs/operations?tab=monitoring").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = SyntheticWaitTimeoutMilliseconds });
            await Assert.ThrowsAsync<TimeoutException>(async () => await loadWait);
        }
        finally
        {
            loadRelease.TrySetResult();
            await loadPage.CloseAsync();
        }

        var (wrongUrlPage, wrongUrlRelease) = await CreateSyntheticPageAsync();
        try
        {
            var wrongUrlWait = wrongUrlPage.WaitForURLAsync(new Regex(monitoringPattern),
                new PageWaitForURLOptions
                {
                    Timeout = SyntheticWaitTimeoutMilliseconds,
                    WaitUntil = WaitUntilState.Commit,
                });
            await wrongUrlPage.GotoAsync(new Uri(baseAddress, "/jobs/operations?tab=failures").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = SyntheticWaitTimeoutMilliseconds });
            await Assert.ThrowsAsync<TimeoutException>(async () => await wrongUrlWait);
        }
        finally
        {
            wrongUrlRelease.TrySetResult();
            await wrongUrlPage.CloseAsync();
        }
    }

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
                var diagnostics = new SanitizedBrowserDiagnostics();
                try
                {
                    var page = await browserContext.NewPageAsync();
                    diagnostics.Attach(page);
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

                    var staticPage = await browserContext.NewPageAsync();
                    diagnostics.Attach(staticPage);
                    try
                    {
                        Assert.IsTrue(diagnostics.BlazorTransportRequestCount > 0,
                            "The interactive page must first demonstrate its real Blazor transport request.");
                        var transportIntercepted = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        await staticPage.RouteAsync("**/_blazor**", async route =>
                        {
                            diagnostics.MarkBlazorTransportIntercepted();
                            transportIntercepted.TrySetResult(true);
                            await route.AbortAsync();
                        });
                        await staticPage.GotoAsync(new Uri(baseAddress, "/jobs/operations?tab=failures").ToString(),
                            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                        await transportIntercepted.Task.WaitAsync(RequestTimeout);
                        Assert.IsTrue(diagnostics.TransportWasIntercepted,
                            "The observed Blazor transport must be intercepted without blocking the boot script.");

                        var staticMonitoringTab = staticPage.GetByRole(AriaRole.Tab, new() { Name = "運用状況" });
                        var staticMonitoringTabCount = await staticMonitoringTab.CountAsync();
                        Assert.AreEqual(1, staticMonitoringTabCount,
                            "The naturally prerendered Monitoring role must remain present for this controlled comparison.");
                        bool? staticMonitoringTabDisabled = staticMonitoringTabCount > 0
                            ? await staticMonitoringTab.IsDisabledAsync()
                            : null;
                        diagnostics.RecordStaticMonitoringTabState(staticMonitoringTabCount, staticMonitoringTabDisabled);
                        Assert.IsTrue(staticMonitoringTabDisabled,
                            "A statically rendered tab must not be actionable before transport readiness.");
                        Assert.AreEqual(0, Volatile.Read(ref runtimeStatusRequestCount),
                            "The controlled pre-interactivity page must not issue a monitoring request.");
                    }
                    finally
                    {
                        await staticPage.CloseAsync();
                    }

                    var interactiveMonitoringTab = page.GetByRole(AriaRole.Tab, new() { Name = "運用状況" });
                    Assert.IsFalse(await interactiveMonitoringTab.IsDisabledAsync(),
                        "The monitoring tab must be enabled only after Blazor interactivity is ready.");
                    diagnostics.MarkInteractiveTabObserved();
                    // The explicit bounded URL wait below is the navigation assertion; the click must not also await full document Load.
#pragma warning disable CS0612 // Playwright 1.62 documents this exception; NoWaitAfter is slated to default true.
                    await interactiveMonitoringTab.ClickAsync(new() { NoWaitAfter = true });
#pragma warning restore CS0612
                    await page.WaitForURLAsync(new Regex(@"/jobs/operations\?tab=monitoring$"),
                        new PageWaitForURLOptions
                        {
                            Timeout = (float)RequestTimeout.TotalMilliseconds,
                            WaitUntil = WaitUntilState.Commit,
                        });
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
                    TestContext.WriteLine(diagnostics.ToSafeSnapshot());
                }
                catch
                {
                    TestContext.WriteLine(diagnostics.ToSafeSnapshot());
                    throw;
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

    private enum BrowserPathCategory { Login, Operations, Other }
    private enum BrowserTabCategory { Monitoring, Failures, Backfills, Revisions, Unknown }
    private enum BrowserRequestCategory { BlazorScript, BlazorTransport, RuntimeStatus, Other }

    private sealed class SanitizedBrowserDiagnostics
    {
        private readonly object _gate = new();
        private readonly List<(BrowserRequestCategory Category, int Status)> _responseStatuses = [];
        private bool _interactiveTabObserved, _navigationReached;
        private bool _transportWasIntercepted;
        private int _staticMonitoringTabCount = -1;
        private bool? _staticMonitoringTabDisabled;
        private int _blazorTransportRequestCount;
        private BrowserPathCategory _path = BrowserPathCategory.Other;
        private BrowserTabCategory _tab = BrowserTabCategory.Unknown;
        private int _consoleErrorCount, _consoleWarningCount, _consoleOtherCount, _pageErrorCount;
        private int _requestFailedBlazorScriptCount, _requestFailedRuntimeStatusCount, _requestFailedOtherCount;
        private int _responseStatusesOmitted;

        internal bool TransportWasIntercepted
        {
            get { lock (_gate) return _transportWasIntercepted; }
        }

        internal int BlazorTransportRequestCount
        {
            get { lock (_gate) return _blazorTransportRequestCount; }
        }

        internal void Attach(IPage page)
        {
            page.Console += (_, message) => ObserveConsole(message.Type, message.Text);
            page.PageError += (_, message) => ObservePageError(message);
            page.Request += (_, request) => ObserveRequest(request.Url);
            page.RequestFailed += (_, request) => ObserveRequestFailure(request.Url);
            page.Response += (_, response) => ObserveResponse(response.Url, response.Status);
            page.FrameNavigated += (_, frame) =>
            {
                if (frame == page.MainFrame) ObserveNavigation(frame.Url);
            };
        }

        internal void MarkBlazorTransportIntercepted()
        {
            lock (_gate) _transportWasIntercepted = true;
        }

        internal void ObserveRequest(string url)
        {
            if (CategorizeRequest(url) != BrowserRequestCategory.BlazorTransport) return;
            lock (_gate) _blazorTransportRequestCount++;
        }

        internal void RecordStaticMonitoringTabState(int count, bool? disabled)
        {
            lock (_gate)
            {
                _staticMonitoringTabCount = Math.Max(0, count);
                _staticMonitoringTabDisabled = disabled;
            }
        }

        internal void MarkInteractiveTabObserved()
        {
            lock (_gate) _interactiveTabObserved = true;
        }

        internal void ObserveConsole(string type, string message)
        {
            lock (_gate)
            {
                switch (type)
                {
                    case "error": _consoleErrorCount++; break;
                    case "warning": _consoleWarningCount++; break;
                    default: _consoleOtherCount++; break;
                }
            }
        }

        internal void ObservePageError(string message)
        {
            lock (_gate) _pageErrorCount++;
        }

        internal void ObserveRequestFailure(string url)
        {
            var category = CategorizeRequest(url);
            lock (_gate)
            {
                switch (category)
                {
                    case BrowserRequestCategory.BlazorScript: _requestFailedBlazorScriptCount++; break;
                    case BrowserRequestCategory.RuntimeStatus: _requestFailedRuntimeStatusCount++; break;
                    default: _requestFailedOtherCount++; break;
                }
            }
        }

        internal void ObserveResponse(string url, int status)
        {
            var category = CategorizeRequest(url);
            lock (_gate)
            {
                if (_responseStatuses.Count < MaximumRecordedResponseStatuses)
                    _responseStatuses.Add((category, status));
                else
                    _responseStatusesOmitted++;
            }
        }

        internal void ObserveNavigation(string url)
        {
            var path = BrowserPathCategory.Other;
            var tab = BrowserTabCategory.Unknown;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                path = uri.AbsolutePath switch
                {
                    "/login" => BrowserPathCategory.Login,
                    "/jobs/operations" => BrowserPathCategory.Operations,
                    _ => BrowserPathCategory.Other,
                };
                if (path == BrowserPathCategory.Operations && uri.Query.Length <= 256)
                {
                    var match = Regex.Match(uri.Query, @"(?:[?&])tab=(monitoring|failures|backfills|revisions)(?:&|$)");
                    tab = match.Success ? match.Groups[1].Value switch
                    {
                        "monitoring" => BrowserTabCategory.Monitoring,
                        "failures" => BrowserTabCategory.Failures,
                        "backfills" => BrowserTabCategory.Backfills,
                        "revisions" => BrowserTabCategory.Revisions,
                        _ => BrowserTabCategory.Unknown,
                    } : BrowserTabCategory.Unknown;
                }
            }

            lock (_gate)
            {
                _path = path;
                _tab = tab;
                _navigationReached |= path == BrowserPathCategory.Operations;
            }
        }

        internal string ToSafeSnapshot()
        {
            lock (_gate)
            {
                var statuses = string.Join(",", _responseStatuses.Select(x => $"{x.Category}:{x.Status}"));
                return $"transportWasIntercepted={_transportWasIntercepted}; blazorTransportRequestCount={_blazorTransportRequestCount}; " +
                    $"staticMonitoringTabCount={_staticMonitoringTabCount}; staticMonitoringTabDisabled={_staticMonitoringTabDisabled?.ToString() ?? "unknown"}; " +
                    $"interactiveTabObserved={_interactiveTabObserved}; " +
                    $"navigationReached={_navigationReached}; path={_path}; tab={_tab}; " +
                    $"consoleErrorCount={_consoleErrorCount}; consoleWarningCount={_consoleWarningCount}; " +
                    $"consoleOtherCount={_consoleOtherCount}; pageErrorCount={_pageErrorCount}; " +
                    $"requestFailedBlazorScriptCount={_requestFailedBlazorScriptCount}; " +
                    $"requestFailedRuntimeStatusCount={_requestFailedRuntimeStatusCount}; " +
                    $"requestFailedOtherCount={_requestFailedOtherCount}; responseStatusesRecorded={_responseStatuses.Count}; " +
                    $"responseStatusesOmitted={_responseStatusesOmitted}; responseStatuses=[{statuses}]";
            }
        }

        private static BrowserRequestCategory CategorizeRequest(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return BrowserRequestCategory.Other;
            return uri.AbsolutePath switch
            {
                "/_framework/blazor.web.js" => BrowserRequestCategory.BlazorScript,
                RuntimePath => BrowserRequestCategory.RuntimeStatus,
                _ when uri.AbsolutePath == "/_blazor" || uri.AbsolutePath.StartsWith("/_blazor/", StringComparison.Ordinal)
                    => BrowserRequestCategory.BlazorTransport,
                _ => BrowserRequestCategory.Other,
            };
        }
    }
}
