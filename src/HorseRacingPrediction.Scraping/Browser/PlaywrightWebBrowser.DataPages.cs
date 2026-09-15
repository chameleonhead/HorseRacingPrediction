using SemanticPageSnapshot = HorseRacingPrediction.Scraping.Browser.Snapshots.PageSnapshot;

namespace HorseRacingPrediction.Scraping.Browser;

public sealed partial class PlaywrightWebBrowser
{
    public Task<string> ClickLinkAsync(PageLinkSnapshot link, CancellationToken cancellationToken = default)
        => ClickLinkCoreAsync(link, includeContent: true, cancellationToken);

    public async Task ClickLinkForSnapshotAsync(PageLinkSnapshot link, CancellationToken cancellationToken = default)
        => _ = await ClickLinkCoreAsync(link, includeContent: false, cancellationToken).ConfigureAwait(false);

    private async Task<string> ClickLinkCoreAsync(
        PageLinkSnapshot link,
        bool includeContent,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await WaitForPageSettledAsync(cancellationToken);
        var anchors = _page.Locator("a[href]");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedHref = ResolveLinkUrl(link.Url);
            var matches = await anchors.EvaluateAllAsync<int[]>(
                """(items, wanted) => items.map((e, index) => ({ e, index })).filter(x => { const e=x.e; const visible=!!(e.offsetWidth||e.offsetHeight||e.getClientRects().length); const region=e.closest('header,[role=banner],#header,.header,[class*=header],#search_modal')?'header':e.closest('footer,[role=contentinfo],#footer')?'footer':'content'; const text=(e.innerText||e.textContent||e.getAttribute('aria-label')||e.getAttribute('title')||'').replace(/\s+/g,' ').trim().toLowerCase(); return visible && (e.getAttribute('href')===wanted.raw || e.href.toLowerCase()===wanted.absolute.toLowerCase()) && region===wanted.region && text===wanted.title; }).map(x => x.index)""",
                new { raw = link.Url, absolute = expectedHref, region = link.Region, title = NormalizeForMatch(link.Title) });
            if (matches.Length != 1)
            {
                if (matches.Length > 1) throw new InvalidOperationException("取得済みリンクが現在ページで一意ではありません: " + link.Title);
                continue;
            }

            var handle = await anchors.Nth(matches[0]).ElementHandleAsync();
            if (handle is null) continue;
            var stillMatches = await handle.EvaluateAsync<bool>(
                """(e, wanted) => e.isConnected && !!(e.offsetWidth||e.offsetHeight||e.getClientRects().length) && (e.getAttribute('href')===wanted.raw || e.href.toLowerCase()===wanted.absolute.toLowerCase()) && (e.closest('header,[role=banner],#header,.header,[class*=header],#search_modal')?'header':e.closest('footer,[role=contentinfo],#footer')?'footer':'content')===wanted.region && (e.innerText||e.textContent||e.getAttribute('aria-label')||e.getAttribute('title')||'').replace(/\s+/g,' ').trim().toLowerCase()===wanted.title""",
                new { raw = link.Url, absolute = expectedHref, region = link.Region, title = NormalizeForMatch(link.Title) });
            if (!stillMatches) continue;
            await handle.ClickAsync().WaitAsync(cancellationToken);
            await _page.WaitForTimeoutAsync(500).WaitAsync(cancellationToken);
            await WaitForPageSettledAsync(cancellationToken);
            return includeContent ? await ReadNormalizedPageTextAsync(cancellationToken) : string.Empty;
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
