namespace HorseRacingPrediction.Scraping.Browser;

/// <summary>
/// モデルへ渡すための Web ページ構造スナップショット。
/// </summary>
public sealed class PageSnapshot
{
    private readonly Lazy<string> _mainText;
    private readonly Lazy<List<string>> _headings;
    private readonly Lazy<List<PageLinkSnapshot>> _allLinks;
    private readonly Lazy<List<PageActionSnapshot>> _allActions;
    private readonly Lazy<List<PageTableSnapshot>> _allTables;
    private readonly Lazy<List<PageFormSnapshot>> _allForms;
    private readonly Lazy<List<PageImageSnapshot>> _allImages;

    public PageSnapshot(
        string url,
        string title,
        List<PageSectionSnapshot> sections)
    {
        Url = url ?? string.Empty;
        Title = title ?? string.Empty;
        Sections = sections ?? [];
        _mainText = new Lazy<string>(
            () => string.Join("\n", Sections
                .Select(section => section.MainText)
                .Where(text => !string.IsNullOrWhiteSpace(text))),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _headings = new Lazy<List<string>>(
            () => Sections
                .SelectMany(section => section.Headings.Count > 0 ? section.Headings : [section.Title])
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _allLinks = new Lazy<List<PageLinkSnapshot>>(
            () => Sections.SelectMany(section => section.Links).ToList(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _allActions = new Lazy<List<PageActionSnapshot>>(
            () => Sections.SelectMany(section => section.Actions).ToList(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _allTables = new Lazy<List<PageTableSnapshot>>(
            () => Sections.SelectMany(section => section.Tables).ToList(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _allForms = new Lazy<List<PageFormSnapshot>>(
            () => Sections.SelectMany(section => section.Forms).ToList(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _allImages = new Lazy<List<PageImageSnapshot>>(
            () => Sections.SelectMany(section => section.Images).ToList(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Url { get; }

    public string Title { get; }

    public List<PageSectionSnapshot> Sections { get; }

    // 既存スクレイパー互換のため、集約済み本文を公開する。
    public string MainText => _mainText.Value;

    // 既存スクレイパー互換のため、見出し一覧を公開する。
    public List<string> Headings => _headings.Value;

    // 既存スクレイパー互換のため、リンク一覧を公開する。
    public List<PageLinkSnapshot> Links => _allLinks.Value;

    // 既存スクレイパー互換のため、アクション一覧を公開する。
    public List<PageActionSnapshot> Actions => _allActions.Value;

    // 既存スクレイパー互換のため、テーブル一覧を公開する。
    public List<PageTableSnapshot> Tables => _allTables.Value;

    // 既存スクレイパー互換のため、フォーム一覧を公開する。
    public List<PageFormSnapshot> Forms => _allForms.Value;

    // 既存スクレイパー互換のため、画像一覧を公開する。
    public List<PageImageSnapshot> Images => _allImages.Value;

    public List<PageLinkSnapshot> GetAllLinks()
        => _allLinks.Value;

    public List<PageActionSnapshot> GetAllActions()
        => _allActions.Value;

    public List<PageTableSnapshot> GetAllTables()
        => _allTables.Value;

    public List<PageFormSnapshot> GetAllForms()
        => _allForms.Value;

    public List<PageImageSnapshot> GetAllImages()
        => _allImages.Value;
}