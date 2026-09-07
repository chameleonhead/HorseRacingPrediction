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
            var href = await anchor.EvaluateAsync<string>("e => e.href");
            if (href != link.Url || !await IsElementRenderedAsync(anchor)) continue;
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

    public async Task<PageSnapshot> GetDataPageSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await WaitForPageSettledAsync(cancellationToken);
        var json = await _page.EvaluateAsync<string>("""
            () => {
              const visible = e => e.getClientRects().length > 0;
              const text = e => (e.innerText || e.textContent || e.getAttribute('aria-label') || '').trim();
              const link = a => ({ Url:a.href, Title:text(a) || [...a.querySelectorAll('img')].map(i=>i.alt).join(' '),
                Region:a.closest('header,[role=banner],#header,.header,[class*=header],#search_modal')?'header':a.closest('footer,[role=contentinfo],#footer')?'footer':'content' });
              return JSON.stringify({ Url:location.href, Title:document.title, Text:document.body.innerText,
                Definitions:[...document.querySelectorAll('dt')].filter(visible).map(d=>[text(d),d.nextElementSibling?.tagName==='DD'?text(d.nextElementSibling):'']),
                Headings:[...document.querySelectorAll('h1,h2,h3')].filter(visible).map(text),
                Links:[...document.querySelectorAll('a[href]')].filter(visible).map(link),
                Tables:[...document.querySelectorAll('table')].filter(visible).map(t=>({
                  Headers:[...t.querySelectorAll('thead tr:last-child th,thead tr:last-child td')].map(text),
                  Rows:[...t.querySelectorAll('tbody tr')].filter(visible).map(r=>[...r.cells].map(c=>({Text:text(c), Links:[...c.querySelectorAll('a[href]')].map(link)})))
                })) });
            }
            """).WaitAsync(cancellationToken);
        var data = System.Text.Json.JsonSerializer.Deserialize<DataPage>(json)!;
        var tables = data.Tables.Select(t => new PageTableSnapshot(t.Headers,
            t.Rows.Select(r => (IReadOnlyList<string>)r.Select(c => c.Text).ToArray()).ToArray(),
            t.Rows.Select(r => (IReadOnlyList<PageTableCellSnapshot>)r.Select(c => new PageTableCellSnapshot(c.Text,
                c.Links.Select(l => new PageDomTextFragment("a", [], l.Title, l.Url)).ToArray())).ToArray()).ToArray())).ToList();
        tables.Insert(0, new PageTableSnapshot(["項目", "値"], data.Definitions.Select(x => (IReadOnlyList<string>)x).ToArray()));
        return new PageSnapshot(data.Url, data.Title, [new PageSectionSnapshot("公開データ", data.Text,
            data.Links.Select(l=>new PageLinkSnapshot(l.Url,l.Title,l.Region)).ToList(), [], tables, data.Headings)]);
    }

    private sealed class DataPage
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
        public List<string> Headings { get; set; } = [];
        public List<DataLink> Links { get; set; } = [];
        public List<DataTable> Tables { get; set; } = [];
        public List<List<string>> Definitions { get; set; } = [];
    }
    private sealed class DataTable
    {
        public List<string> Headers { get; set; } = [];
        public List<List<DataCell>> Rows { get; set; } = [];
    }
    private sealed class DataCell
    {
        public string Text { get; set; } = "";
        public List<DataLink> Links { get; set; } = [];
    }
    private sealed class DataLink
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public string Region { get; set; } = "content";
    }
}
