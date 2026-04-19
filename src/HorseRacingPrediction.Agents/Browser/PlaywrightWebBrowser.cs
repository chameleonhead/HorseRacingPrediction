using System.Text.Json;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Agents.Browser;

/// <summary>
/// Microsoft.Playwright を使ったセッションベースのブラウザ実装。
/// セッション中は同一の <see cref="IPage"/> を維持し、ナビゲーション・クリック・
/// テキスト取得などの操作を逐次実行する。
/// </summary>
public sealed class PlaywrightWebBrowser : IWebBrowser
{
    private const int MaxContentLength = 12_000;
    private const int DefaultTimeoutMs = 30_000;
    private const int MaxRetries = 2;

    private const string UserAgentString =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

    /// <summary>
    /// ページからリンクを抽出する JavaScript。
    /// 検索結果ページ（Bing / Google）と一般ページの両方に対応する。
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

            // Generic page links fallback
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
    private readonly IBrowserContext _context;
    private readonly IPage _page;

    private PlaywrightWebBrowser(
        IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page)
    {
        _playwright = playwright;
        _browser = browser;
        _context = context;
        _page = page;
        _page.SetDefaultTimeout(DefaultTimeoutMs);
    }

    /// <inheritdoc />
    public string? CurrentUrl => _page.Url is "about:blank" ? null : _page.Url;

    /// <summary>
    /// PlaywrightWebBrowser のインスタンスを非同期で生成する。
    /// ブラウザ・コンテキスト・ページをすべて初期化する。
    /// </summary>
    public static async Task<PlaywrightWebBrowser> CreateAsync()
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = ["--disable-blink-features=AutomationControlled"]
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = UserAgentString,
            Locale = "ja-JP",
            TimezoneId = "Asia/Tokyo",
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
        });
        var page = await context.NewPageAsync();
        return new PlaywrightWebBrowser(playwright, browser, context, page);
    }

    /// <inheritdoc />
    public async Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        var lastException = default(Exception);
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await _page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = DefaultTimeoutMs
                });

                // HTTP エラーステータスをチェックし、エージェントに明確なメッセージを返す
                if (response is not null && response.Status >= 400)
                {
                    return $"HTTP {response.Status} エラー: {url} へのアクセスが拒否されました。" +
                           $"この URL は直接アクセスできません。" +
                           $"別のページからリンクをたどってください。";
                }

                await WaitForNetworkIdleAsync();
                return await ReadPageTextAsync();
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

        return $"ページの読み込みに失敗しました: {url} — " +
               $"{lastException?.Message ?? "不明なエラー"}。" +
               $"別の URL を試すか、リンク一覧から別のページを選んでください。";
    }

    /// <inheritdoc />
    public async Task<string> ClickAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // まずリンク、次にボタン、最後に任意の要素を試す
        var selectors = new[]
        {
            $"a:has-text(\"{EscapeSelector(text)}\")",
            $"button:has-text(\"{EscapeSelector(text)}\")",
            $"[role=\"tab\"]:has-text(\"{EscapeSelector(text)}\")",
            $"[role=\"button\"]:has-text(\"{EscapeSelector(text)}\")",
            $"text=\"{EscapeSelector(text)}\"",
        };

        foreach (var selector in selectors)
        {
            try
            {
                var locator = _page.Locator(selector).First;
                if (await locator.CountAsync() == 0) continue;
                if (!await locator.IsVisibleAsync()) continue;

                await locator.ScrollIntoViewIfNeededAsync();
                await locator.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });

                // クリック後の状態安定を待つ
                await WaitForStableStateAsync();
                return await ReadPageTextAsync();
            }
            catch
            {
                // 次のセレクタを試す
            }
        }

        throw new InvalidOperationException(
            $"クリック可能な要素が見つかりませんでした: \"{text}\"");
    }

    /// <inheritdoc />
    public async Task<string> GetPageContentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await ReadPageTextAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResultLink>> GetLinksAsync(
        int maxResults = 10, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await ExtractLinksFromCurrentPageAsync(maxResults);
    }

    /// <inheritdoc />
    public async Task<string> SearchAsync(
        string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var isAlreadyOnBing = _page.Url?.Contains("bing.com/search", StringComparison.OrdinalIgnoreCase) == true;

        if (isAlreadyOnBing)
        {
            // 既に Bing 上にいる場合は検索ボックスを使う（URL 直接遷移は Bot 判定されやすい）
            var delay = Random.Shared.Next(2_000, 5_000);
            await Task.Delay(delay, cancellationToken);

            var searchBox = _page.Locator("#sb_form_q, input[name='q']").First;
            await searchBox.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
            await searchBox.ClearAsync();
            await searchBox.TypeAsync(query, new LocatorTypeOptions { Delay = 50 });
            await searchBox.PressAsync("Enter");
        }
        else
        {
            // 初回検索: URL 直接遷移で Bing に移動
            var encodedQuery = Uri.EscapeDataString(query);
            var searchUrl = $"https://www.bing.com/search?q={encodedQuery}&setlang=ja&cc=JP";

            await _page.GotoAsync(searchUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = DefaultTimeoutMs
            });

            // 同意ダイアログが表示された場合は承認する
            try
            {
                var consentButton = _page.Locator("#bnp_btn_accept, button[id*='accept'], #bnp_ttc_close").First;
                if (await consentButton.IsVisibleAsync())
                {
                    await consentButton.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
                    await Task.Delay(1_000, cancellationToken);
                }
            }
            catch
            {
                // 同意ダイアログがない場合は無視
            }
        }

        // 検索結果の表示を待つ
        try
        {
            await _page.WaitForSelectorAsync(".b_algo, #b_results .b_algo, #rso",
                new PageWaitForSelectorOptions { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            // セレクタが見つからなくても続行
        }

        await WaitForNetworkIdleAsync();

        // 検索結果ページのテキストをそのまま返す（AI がリンクを判断する）
        return await ReadPageTextAsync();
    }

    /// <inheritdoc />
    public async Task<string> GoBackAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _page.GoBackAsync(new PageGoBackOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = DefaultTimeoutMs
        });

        await WaitForNetworkIdleAsync();
        return await ReadPageTextAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _context.CloseAsync();
        await _browser.CloseAsync();
        _playwright.Dispose();
    }

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private async Task<string> ReadPageTextAsync()
    {
        // main/article 要素があればそちらを優先し、ナビゲーションノイズを減らす
        var text = await _page.EvaluateAsync<string>("""
            () => {
                const main = document.querySelector('main, article, [role="main"], #main, #content, .main-content');
                if (main && main.innerText && main.innerText.trim().length > 200) {
                    return main.innerText;
                }
                return document.body ? document.body.innerText : '';
            }
            """);

        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 連続空行を圧縮
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\r?\n){3,}", "\n\n");

        return text.Length > MaxContentLength ? text[..MaxContentLength] : text;
    }

    private async Task<IReadOnlyList<SearchResultLink>> ExtractLinksFromCurrentPageAsync(int maxResults)
    {
        // 検索結果がある場合は少し待つ
        try
        {
            await _page.WaitForSelectorAsync(".b_algo, #rso",
                new PageWaitForSelectorOptions { Timeout = 3_000 });
        }
        catch (TimeoutException)
        {
            // 検索結果ページでない場合もある
        }

        var json = await _page.EvaluateAsync<JsonElement>(ExtractLinksScript, maxResults);

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

    private async Task WaitForNetworkIdleAsync()
    {
        try
        {
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            // NetworkIdle に達しなくても続行
        }
    }

    private async Task WaitForStableStateAsync()
    {
        try
        {
            await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions { Timeout = 5_000 });
        }
        catch (TimeoutException) { }

        try
        {
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = 5_000 });
        }
        catch (TimeoutException) { }
    }

    private static string EscapeSelector(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
