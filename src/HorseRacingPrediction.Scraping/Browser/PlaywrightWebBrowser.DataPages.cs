using SemanticPageSnapshot = HorseRacingPrediction.Scraping.Browser.Snapshots.PageSnapshot;

namespace HorseRacingPrediction.Scraping.Browser;

public sealed partial class PlaywrightWebBrowser
{
    public async Task<string> ClickLinkAsync(PageLinkSnapshot link, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await WaitForPageSettledAsync(cancellationToken);
        var anchors = _page.Locator("a[href]");
        for (var index = 0; index < await anchors.CountAsync(); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var anchor = anchors.Nth(index);
            var rawHref = await anchor.GetAttributeAsync("href");
            var resolvedHref = await anchor.EvaluateAsync<string>("e => e.href");
            var expectedHref = ResolveLinkUrl(link.Url);
            if (!string.Equals(rawHref, link.Url, StringComparison.Ordinal) &&
                !string.Equals(resolvedHref, expectedHref, StringComparison.OrdinalIgnoreCase)) continue;
            if (!await IsElementRenderedAsync(anchor)) continue;
            var region = await anchor.EvaluateAsync<string>("e => e.closest('header,[role=banner],#header,.header,[class*=header],#search_modal') ? 'header' : e.closest('footer,[role=contentinfo],#footer') ? 'footer' : 'content'");
            if (region != link.Region) continue;
            if (NormalizeForMatch(await GetLocatorTextAsync(anchor)) != NormalizeForMatch(link.Title)) continue;
            await anchor.ClickAsync().WaitAsync(cancellationToken);
            await _page.WaitForTimeoutAsync(500).WaitAsync(cancellationToken);
            await WaitForPageSettledAsync(cancellationToken);
            return await GetPageContentAsync(cancellationToken);
        }
        throw new InvalidOperationException("取得済みリンクが現在ページに見つかりません: " + link.Title);
    }

    private string ResolveLinkUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute.AbsoluteUri;
        }

        return Uri.TryCreate(CurrentUrl, UriKind.Absolute, out var current) &&
               Uri.TryCreate(current, url, out var resolved)
            ? resolved.AbsoluteUri
            : url;
    }

    public Task<SemanticPageSnapshot> GetDataPageSnapshotAsync(CancellationToken cancellationToken = default)
        => GetPageSnapshotAsync(cancellationToken);
}
