namespace HorseRacingPrediction.Scraping.Browser;

/// <summary>
/// ページ内の1セクションを表すスナップショット。
/// </summary>
public sealed class PageSectionSnapshot
{
    private readonly List<string> _headings;
    private readonly List<PageLinkSnapshot> _links;
    private readonly List<PageActionSnapshot> _actions;
    private readonly List<PageTableSnapshot> _tables;
    private readonly List<PageFormSnapshot> _forms;
    private readonly List<PageImageSnapshot> _images;

    public PageSectionSnapshot(
        string title,
        string mainText,
        List<PageLinkSnapshot> links,
        List<PageActionSnapshot> actions,
        List<PageTableSnapshot> tables,
        List<string> headings,
        List<PageFormSnapshot>? forms = null,
        List<PageImageSnapshot>? images = null)
    {
        Title = title ?? string.Empty;
        MainText = mainText ?? string.Empty;
        _headings = headings?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];

        _links = links ?? [];
        _actions = actions ?? [];
        _tables = tables ?? [];
        _forms = forms ?? [];
        _images = images ?? [];
    }

    public string Title { get; }

    public string MainText { get; }

    public List<string> Headings => _headings;

    public List<PageLinkSnapshot> Links => _links;

    public List<PageActionSnapshot> Actions => _actions;

    public List<PageTableSnapshot> Tables => _tables;

    public List<PageFormSnapshot> Forms => _forms;

    public List<PageImageSnapshot> Images => _images;
}
