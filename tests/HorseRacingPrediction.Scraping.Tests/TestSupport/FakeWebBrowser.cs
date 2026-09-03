using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Scraping.Tests.TestSupport;

/// <summary>
/// JraNavigator/JraPageReaderのテスト用フェイク。URLごとにスナップショット・リンクを
/// 設定でき、実ブラウザーなしでナビゲーションフローを検証できる。
/// </summary>
internal sealed class FakeWebBrowser : IWebBrowser
{
    private readonly Dictionary<string, PageSnapshot> _snapshotsByUrl = new();
    private readonly Dictionary<string, List<PageLinkSnapshot>> _linksByUrl = new();

    public string? CurrentUrl { get; private set; }

    public List<string> NavigatedUrls { get; } = [];

    /// <summary>
    /// テストの初期状態を設定する。NavigatedUrlsには記録しない。
    /// </summary>
    public void SetCurrentUrl(string? url)
        => CurrentUrl = url;

    public void SetSnapshot(string url, PageSnapshot snapshot)
        => _snapshotsByUrl[url] = snapshot;

    public void SetLinks(string url, IEnumerable<PageLinkSnapshot> links)
        => _linksByUrl[url] = links.ToList();

    public Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        CurrentUrl = url;
        NavigatedUrls.Add(url);
        return Task.FromResult(string.Empty);
    }

    public Task<PageSnapshot> GetPageSnapshotAsync(
        int maxLinks = 0,
        CancellationToken cancellationToken = default)
    {
        var url = CurrentUrl ?? string.Empty;

        if (_snapshotsByUrl.TryGetValue(url, out var snapshot))
        {
            return Task.FromResult(snapshot);
        }

        return Task.FromResult(new PageSnapshot(url, string.Empty, []));
    }

    public Task<IReadOnlyList<PageLinkSnapshot>> GetLinksAsync(
        int maxResults = 0,
        CancellationToken cancellationToken = default)
    {
        var url = CurrentUrl ?? string.Empty;

        if (_linksByUrl.TryGetValue(url, out var links))
        {
            return Task.FromResult<IReadOnlyList<PageLinkSnapshot>>(links);
        }

        return Task.FromResult<IReadOnlyList<PageLinkSnapshot>>([]);
    }

    public Task<string> ClickAsync(string text, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<string> SelectOptionAsync(
        string fieldText,
        string optionText,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<string> ClickActionInSectionAsync(
        string sectionText,
        string actionText,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<string> GetPageContentAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);

    public Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<string> GoBackAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}
