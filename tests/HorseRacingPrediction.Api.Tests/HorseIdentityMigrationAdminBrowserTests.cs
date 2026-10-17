using System.Diagnostics;
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
        using var process = StartApiProcess(repositoryRoot, stores, protectionDirectory, localApiKey, baseAddress);
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
                Assert.IsTrue(HasDisabledPrerenderedCandidateCheckbox(prerenderedHtml),
                    "Candidate selection must remain disabled until its interactive handler is ready.");
                Assert.IsTrue(await page.GetByText("生年月日とプロフィールの根拠が不足しています。").First.IsVisibleAsync());

                await page.GetByRole(AriaRole.Button, new() { Name = "保存先を診断" }).ClickAsync();
                await page.GetByText("診断の読み取りは完了しました", new() { Exact = false }).WaitForAsync(new LocatorWaitForOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                Assert.IsTrue(await page.GetByText("候補を選ばずに保存先の構造だけを確認できます。", new() { Exact = false }).IsVisibleAsync());
                Assert.IsTrue(await page.GetByRole(AriaRole.Button, new() { Name = "安全な診断JSONをコピー" }).IsVisibleAsync());

                await page.WaitForFunctionAsync("""
                    () => {
                        const checkbox = document.querySelector('.migration-grid fluent-checkbox');
                        return checkbox && !checkbox.hasAttribute('disabled') && checkbox.getAttribute('aria-disabled') !== 'true';
                    }
                    """, new PageWaitForFunctionOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });
                await page.GetByLabel("登録馬名またはIDで検索").FillAsync("Fixture Horse");
                await page.GetByRole(AriaRole.Button, new() { Name = "登録馬を検索" }).ClickAsync();
                var horseSelector = page.GetByRole(AriaRole.Combobox, new() { Name = "追加する旧ID" });
                await horseSelector.FocusAsync();
                Assert.IsTrue(await horseSelector.EvaluateAsync<bool>("element => document.activeElement === element || (element.shadowRoot !== null && element.shadowRoot.activeElement !== null)"),
                    "Keyboard focus must reach the existing-horse selector.");
                await horseSelector.PressAsync("ArrowDown");
                await horseSelector.PressAsync("Enter");
                await Assertions.Expect(horseSelector).ToHaveAttributeAsync("current-value", AddedHorseId,
                    new LocatorAssertionsToHaveAttributeOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });
                Assert.AreEqual(AddedHorseId, await horseSelector.GetAttributeAsync("current-value"),
                    "Keyboard selection must choose the explicit registered HorseId, not the matching preset display name.");
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
                var previewButton = page.GetByRole(AriaRole.Button, new() { Name = "選択内容をプレビュー" });
                await Assertions.Expect(previewButton).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                await previewButton.ClickAsync();
                await page.GetByText("表示された候補と保存状態を確認してください").WaitForAsync(new LocatorWaitForOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                Assert.IsTrue(await page.GetByText("対象行").First.IsVisibleAsync());
                await AssertGridCellsAreFullyVisibleAsync(page.Locator(".migration-candidates .migration-grid"));
                var previewGrids = page.Locator(".migration-preview .migration-grid");
                for (var index = 0; index < await previewGrids.CountAsync(); index++)
                    await AssertGridCellsAreFullyVisibleAsync(previewGrids.Nth(index));
                var candidateGrid = page.Locator(".migration-candidates .migration-grid");
                Assert.IsTrue(await candidateGrid.GetByText(AddedHorseId, new() { Exact = true }).IsVisibleAsync());
                Assert.IsTrue(await candidateGrid.GetByText($"Fixture Horse → jra-horse:{AddedJraNumber}", new() { Exact = true }).IsVisibleAsync());
                await page.Locator(".migration-candidates").ScrollIntoViewIfNeededAsync();
                await page.ScreenshotAsync(new PageScreenshotOptions
                { Path = Path.Combine(screenshotDirectory, "desktop-preview.png") });

                await page.GetByRole(AriaRole.Button, new() { Name = "この固定内容を実行依頼として保存" }).ClickAsync();
                var confirmSaveButton = page.GetByRole(AriaRole.Button, new() { Name = "この内容を実行依頼として保存" });
                await Assertions.Expect(confirmSaveButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                await confirmSaveButton.ClickAsync();
                await page.GetByText("まだ実行されていません").WaitForAsync(new LocatorWaitForOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
                Assert.IsTrue(await page.GetByText("まだ実行されていません").IsVisibleAsync(),
                    "Saving an operation must display a receipt without starting the workflow.");
                Assert.IsTrue(page.Url.Contains("/settings/horse-id-migration?operationId=", StringComparison.Ordinal),
                    "The saved GUID should be addressable through the same Settings URL without fetching status automatically.");
                Assert.IsTrue(await page.GetByText("既存の GitHub Actions の app-deploy workflow を手動 dispatch").IsVisibleAsync());
                await page.SetViewportSizeAsync(390, 844);
                Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"),
                    "The mobile status/reconnect screen must not overflow horizontally.");
                Assert.IsTrue(await page.Locator(".migration-cards").First.IsVisibleAsync());
                Assert.IsFalse(await page.Locator(".migration-grid").First.IsVisibleAsync());
                await Assertions.Expect(page.Locator(".migration-candidates .migration-cards").GetByText(AddedHorseId, new() { Exact = true }))
                    .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = (float)RequestTimeout.TotalMilliseconds });
                await AssertTextFitsItsMobileCellAsync(
                    page.Locator(".migration-candidates .migration-cards").GetByText(AddedHorseId, new() { Exact = true }));
                var hasVisibleFullFingerprint = await page.Locator(".migration-preview .migration-cards dd").EvaluateAllAsync<bool>("""
                    cells => cells.some(cell => {
                        const value = cell.textContent.trim();
                        if (!/^[A-Fa-f0-9]{64}$/.test(value)) return false;
                        const rect = cell.getBoundingClientRect();
                        const style = getComputedStyle(cell);
                        return rect.width > 0 && rect.height > 0 && style.whiteSpace !== 'nowrap'
                            && cell.scrollWidth <= cell.clientWidth + 1 && cell.scrollHeight <= cell.clientHeight + 1;
                    })
                    """);
                Assert.IsTrue(hasVisibleFullFingerprint,
                    "At 390px, a complete 64-character preview hash must remain visible and wrap within its card cell.");

                await page.GetByRole(AriaRole.Button, new() { Name = "状態を更新" }).ClickAsync();
                await page.GetByText("手動操作待ち").WaitForAsync(new LocatorWaitForOptions
                { Timeout = (float)RequestTimeout.TotalMilliseconds });
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
        string protectionDirectory, string localApiKey, Uri baseAddress)
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

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
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

    private static bool HasDisabledPrerenderedCandidateCheckbox(string html)
    {
        foreach (Match match in Regex.Matches(html,
                     @"<(?<tag>fluent-checkbox|button)\b(?<attributes>[^>]*)>(?<content>.*?)</\k<tag>\s*>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var attributes = match.Groups["attributes"].Value;
            var candidate = attributes.Contains("ナムラアラバイ", StringComparison.Ordinal)
                || match.Groups["content"].Value.Contains("ナムラアラバイ", StringComparison.Ordinal);
            if (!candidate) continue;
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
