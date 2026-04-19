using Microsoft.Playwright;

namespace HorseRacingPrediction.Agents.Browser;

/// <summary>
/// Microsoft.Playwright を使って実際のブラウザを操作し、ページ本文を取得する実装。
/// JavaScript レンダリングが必要なページにも対応する。
/// </summary>
public sealed class PlaywrightWebBrowser : IWebBrowser, IAsyncDisposable
{
    private const int MaxContentLength = 10_000;
    private const int TimeoutMs = 30_000;
    private const int MaxRetries = 2;

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

    private async Task<string> FetchOnceAsync(string url, CancellationToken cancellationToken)
    {
        await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (compatible; HorseRacingPredictionBot/1.0)"
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
