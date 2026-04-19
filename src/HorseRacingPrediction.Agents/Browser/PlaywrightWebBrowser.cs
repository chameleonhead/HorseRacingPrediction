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
                if (!url.startsWith('http') || seen.has(url)) continue;
                seen.add(url);
                results.push({ url, title: (anchor.textContent || '').trim() });
            }

            // Google organic results (fallback)
            if (results.length === 0) {
                for (const item of document.querySelectorAll('#rso > div')) {
                    if (results.length >= maxResults) break;
                    const a = item.querySelector('a[href^="http"]');
                    if (!a || a.href.includes('google.')) continue;
                    const h3 = a.querySelector('h3');
                    const url = a.href;
                    if (seen.has(url)) continue;
                    seen.add(url);
                    results.push({ url, title: (h3 ? h3.textContent : '').trim() });
                }
            }

            return results;
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
            Headless = true
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
        await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = UserAgentString
        });

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(TimeoutMs);

        try
        {
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = TimeoutMs
            });

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
        finally
        {
            await page.CloseAsync();
        }
    }

    private async Task<string> FetchOnceAsync(string url, CancellationToken cancellationToken)
    {
        await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = UserAgentString
        });

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(TimeoutMs);

        try
        {
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = TimeoutMs
            });

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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }
}
