using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Scraping.Tests.TestSupport;

/// <summary>
/// JraNavigator/JraPageReaderのテスト用フェイク。URLごとにスナップショット・リンクを
/// 設定でき、実ブラウザーなしでナビゲーションフローを検証できる。
/// </summary>
internal sealed class FakeWebBrowser : IWebBrowser
{
    public bool IsDisposed { get; private set; }


    private readonly Dictionary<string, PageSnapshot> _snapshotsByUrl = new();
    private readonly Dictionary<string, List<PageLinkSnapshot>> _linksByUrl = new();
    private readonly Dictionary<string, List<PageFormSnapshot>> _formsByUrl = new();
    private readonly Dictionary<string, string> _clickDestinationsByText = new();

    /// <summary>
    /// SubmitFormAsyncが呼ばれた際に遷移する先のURL。テストで事前に設定する。
    /// </summary>
    private string? _submitDestinationUrl;

    public string? CurrentUrl { get; private set; }

    public List<string> NavigatedUrls { get; } = [];

    public List<string> SetFieldCalls { get; } = [];

    public List<(string FieldLabelOrName, string OptionText)> SelectOptionCalls { get; } = [];

    public int SubmitFormCallCount { get; private set; }

    /// <summary>
    /// テストの初期状態を設定する。NavigatedUrlsには記録しない。
    /// </summary>
    public void SetCurrentUrl(string? url)
        => CurrentUrl = url;

    public void SetSnapshot(string url, PageSnapshot snapshot)
        => _snapshotsByUrl[url] = snapshot;

    public void SetLinks(string url, IEnumerable<PageLinkSnapshot> links)
        => _linksByUrl[url] = links.ToList();

    public void SetForms(string url, IEnumerable<PageFormSnapshot> forms)
        => _formsByUrl[url] = forms.ToList();

    /// <summary>
    /// ClickAsyncでtextが指定された際に遷移する先のURLを設定する。
    /// 実サイトのメニュー項目・開催選択ボタンはhrefを持たないJS要素のため、
    /// ナビゲーターはリンク探索ではなくClickAsyncで遷移する（Task16実サイト確認で判明）。
    /// </summary>
    public void SetClickDestination(string text, string url)
        => _clickDestinationsByText[text] = url;

    public List<string> ClickedTexts { get; } = [];

    /// <summary>
    /// SubmitFormAsync呼び出し後に遷移する先のURLを設定する。
    /// </summary>
    public void SetSubmitDestination(string url)
        => _submitDestinationUrl = url;

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
    {
        ClickedTexts.Add(text);

        if (_clickDestinationsByText.TryGetValue(text, out var url))
        {
            CurrentUrl = url;
            NavigatedUrls.Add(url);
            return Task.FromResult(string.Empty);
        }

        throw new InvalidOperationException($"No click destination configured for text: {text}");
    }

    public Task<string> SelectOptionAsync(
        string fieldText,
        string optionText,
        CancellationToken cancellationToken = default)
    {
        SelectOptionCalls.Add((fieldText, optionText));
        return Task.FromResult(string.Empty);
    }

    public Task<IReadOnlyList<PageFormSnapshot>> GetFormsAsync(
        CancellationToken cancellationToken = default)
    {
        var url = CurrentUrl ?? string.Empty;

        if (_formsByUrl.TryGetValue(url, out var forms))
        {
            return Task.FromResult<IReadOnlyList<PageFormSnapshot>>(forms);
        }

        return Task.FromResult<IReadOnlyList<PageFormSnapshot>>([]);
    }

    public Task<string> SetFieldValueAsync(
        string fieldLabelOrName,
        string value,
        CancellationToken cancellationToken = default)
    {
        SetFieldCalls.Add(fieldLabelOrName);
        return Task.FromResult(string.Empty);
    }

    public Task<string> SubmitFormAsync(
        string? formLabel = null,
        CancellationToken cancellationToken = default)
    {
        SubmitFormCallCount++;

        if (_submitDestinationUrl is not null)
        {
            CurrentUrl = _submitDestinationUrl;
            NavigatedUrls.Add(_submitDestinationUrl);
        }

        return Task.FromResult(string.Empty);
    }

    public Task<string> ClickActionInSectionAsync(
        string sectionText,
        string actionText,
        CancellationToken cancellationToken = default)
    {
        ClickedTexts.Add(actionText);

        if (_clickDestinationsByText.TryGetValue(actionText, out var url))
        {
            CurrentUrl = url;
            NavigatedUrls.Add(url);
            return Task.FromResult(string.Empty);
        }

        if (_submitDestinationUrl is not null)
        {
            CurrentUrl = _submitDestinationUrl;
            NavigatedUrls.Add(_submitDestinationUrl);
            return Task.FromResult(string.Empty);
        }

        throw new InvalidOperationException(
            $"No click destination configured for action: {actionText} (section: {sectionText})");
    }

    public Task<string> GetPageContentAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);

    public Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<string> GoBackAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}
