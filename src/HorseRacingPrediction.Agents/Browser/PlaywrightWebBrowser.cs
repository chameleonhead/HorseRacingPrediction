using System.Text.Json;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Agents.Browser;

/// <summary>
/// Microsoft.Playwright を使って実際のブラウザを操作し、ページ本文を取得する実装。
/// JavaScript レンダリングが必要なページにも対応する。
/// </summary>
public sealed class PlaywrightWebBrowser : IWebBrowser, IAsyncDisposable
{
    private const int MaxContentLength = 30_000;
    private const int TimeoutMs = 30_000;
    private const int MaxRetries = 2;

    private const string UserAgentString =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

    /// <summary>
    /// 検索結果ページからリンクを抽出する JavaScript。
    /// Bing と Google の両方に対応し、Bing のリダイレクト URL をデコードする。
    /// </summary>
    private const string ExtractLinksScript = """
        (maxResults) => {
            const results = [];
            const seen = new Set();

            const pushLink = (href, title) => {
                if (!href) return;
                try {
                    const resolved = new URL(href, location.href);
                    if (!/^https?:$/i.test(resolved.protocol)) return;
                    const url = resolved.href;
                    if (seen.has(url)) return;
                    seen.add(url);
                    results.push({ url, title: (title || '').trim() });
                } catch (e) {}
            };

            // Bing organic results
            for (const item of document.querySelectorAll('.b_algo')) {
                if (results.length >= maxResults) break;
                const anchor = item.querySelector('h2 a');
                if (!anchor) continue;
                let url = anchor.href;
                try {
                    const u = new URL(url);
                    const enc = u.searchParams.get('u');
                    if (enc && enc.startsWith('a1')) {
                        url = atob(enc.substring(2));
                    }
                } catch (e) {}
                pushLink(url, anchor.textContent || '');
            }

            // Google organic results
            if (results.length === 0) {
                for (const item of document.querySelectorAll('#rso a[href]')) {
                    if (results.length >= maxResults) break;
                    const href = item.getAttribute('href') || '';
                    if (href.includes('google.')) continue;
                    const h3 = item.querySelector('h3');
                    pushLink(href, (h3 ? h3.textContent : item.textContent) || '');
                }
            }

            // Generic page links fallback for on-site exploration
            if (results.length === 0) {
                const ignore = ['ログイン', 'サインイン', 'privacy', 'cookie', '利用規約', 'お問い合わせ'];
                for (const anchor of document.querySelectorAll('a[href]')) {
                    if (results.length >= maxResults) break;
                    const title = (anchor.textContent || anchor.getAttribute('aria-label') || anchor.title || '').trim();
                    if (title.length < 2) continue;
                    const lower = title.toLowerCase();
                    if (ignore.some(x => lower.includes(x.toLowerCase()))) continue;
                    pushLink(anchor.getAttribute('href'), title);
                }
            }

            return results.slice(0, maxResults);
        }
        """;

    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;

    private PlaywrightWebBrowser(IPlaywright playwright, IBrowser browser)
    {
        _playwright = playwright;
        _browser = browser;
    }

    /// <summary>
    /// PlaywrightWebBrowser のインスタンスを非同期で生成する。
    /// </summary>
    public static async Task<PlaywrightWebBrowser> CreateAsync()
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ["--disable-blink-features=AutomationControlled"]
        });
        return new PlaywrightWebBrowser(playwright, browser);
    }

    /// <inheritdoc />
    public async Task<string> FetchTextAsync(string url, CancellationToken cancellationToken = default)
    {
        var lastException = default(Exception);
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await FetchOnceAsync(url, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }

        throw new InvalidOperationException(
            $"URL の取得に失敗しました: {url}",
            lastException);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResultLink>> ExtractLinksAsync(
        string url,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync();

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(TimeoutMs);

        try
        {
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = TimeoutMs
            });

            return await ExtractSearchResultsAsync(page, maxResults);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResultLink>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync();

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(TimeoutMs);

        try
        {
            // Bing トップページに移動
            await page.GotoAsync("https://www.bing.com", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = TimeoutMs
            });

            // 検索ボックスを探してクエリを入力
            var searchBox = page.Locator("textarea[name='q'], input[name='q']").First;
            await searchBox.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
            await searchBox.ClickAsync();
            await searchBox.FillAsync(query);
            await page.Keyboard.PressAsync("Enter");

            // 検索結果が表示されるまで待機
            try
            {
                await page.WaitForSelectorAsync(".b_algo, #rso", new PageWaitForSelectorOptions
                {
                    Timeout = 15_000
                });
            }
            catch (TimeoutException)
            {
                // セレクタが見つからなくても続行
            }

            return await ExtractSearchResultsAsync(page, maxResults);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private async Task<string> FetchOnceAsync(string url, CancellationToken cancellationToken)
    {
        await using var context = await CreateContextAsync();

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(TimeoutMs);

        try
        {
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = TimeoutMs
            });

            // コンテンツが読み込まれるまで少し待つ
            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                    new PageWaitForLoadStateOptions { Timeout = 10_000 });
            }
            catch (TimeoutException)
            {
                // NetworkIdle に達しなくても続行（検索エンジン等）
            }

            await PreparePageForExtractionAsync(page);

            var text = await page.EvaluateAsync<string>(
                "() => document.body ? document.body.innerText : ''");

            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Length > MaxContentLength
                ? text[..MaxContentLength]
                : text;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// 検索結果ページからリンクを抽出する共通メソッド。
    /// </summary>
    private static async Task<IReadOnlyList<SearchResultLink>> ExtractSearchResultsAsync(
        IPage page, int maxResults)
    {
        // 検索結果が描画されるまで待機
        try
        {
            await page.WaitForSelectorAsync(".b_algo, #rso", new PageWaitForSelectorOptions
            {
                Timeout = 10_000
            });
        }
        catch (TimeoutException)
        {
            // セレクタが見つからなくても続行
        }

        var json = await page.EvaluateAsync<JsonElement>(
            ExtractLinksScript, maxResults);

        var links = new List<SearchResultLink>();
        if (json.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in json.EnumerateArray())
            {
                var linkUrl = item.GetProperty("url").GetString();
                var title = item.GetProperty("title").GetString();
                if (!string.IsNullOrEmpty(linkUrl))
                {
                    links.Add(new SearchResultLink(linkUrl, title ?? ""));
                }
            }
        }

        return links;
    }

    private static async Task<bool> TryClickFirstVisibleAsync(IPage page, IEnumerable<string> selectors)
    {
        foreach (var selector in selectors)
        {
            try
            {
                var locator = page.Locator(selector).First;
                if (await locator.CountAsync() == 0 || !await locator.IsVisibleAsync())
                {
                    continue;
                }

                await locator.ScrollIntoViewIfNeededAsync();
                await locator.ClickAsync(new LocatorClickOptions
                {
                    Timeout = 5_000
                });
                return true;
            }
            catch
            {
                // 次の候補を試す
            }
        }

        return false;
    }

    private static async Task PreparePageForExtractionAsync(IPage page)
    {
        var bodyText = await page.EvaluateAsync<string>("() => document.body ? document.body.innerText : ''");
        if (HasInformativeContent(bodyText))
        {
            return;
        }

        var clicked = await TryClickFirstVisibleAsync(page,
        [
            "a:has-text(\"続きを読む\")",
            "button:has-text(\"続きを読む\")",
            "a:has-text(\"もっと見る\")",
            "button:has-text(\"もっと見る\")",
            "a:has-text(\"詳細\")",
            "button:has-text(\"詳細\")",
            "a:has-text(\"More\")",
            "button:has-text(\"More\")",
            "a:has-text(\"Read more\")",
            "button:has-text(\"Read more\")",
            "a:has-text(\"出走馬\")",
            "button:has-text(\"出走馬\")",
            "a:has-text(\"出馬表\")",
            "button:has-text(\"出馬表\")",
            "a:has-text(\"枠順\")",
            "button:has-text(\"枠順\")"
        ]);

        if (!clicked)
        {
            return;
        }

        try
        {
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            // DOMContentLoaded を待てなくても続行
        }

        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            // 非同期通信継続中でも本文抽出へ進む
        }
    }

    private static bool HasInformativeContent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Length >= 1500 ||
            ContainsAny(text, "価格", "料金", "概要", "詳細", "手順", "インストール", "馬名", "騎手", "斤量", "枠番");
    }

    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ボット検知を回避するためのリアルなブラウザコンテキストを作成する。
    /// </summary>
    private async Task<IBrowserContext> CreateContextAsync()
    {
        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = UserAgentString,
            Locale = "ja-JP",
            TimezoneId = "Asia/Tokyo",
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
        });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }
}
