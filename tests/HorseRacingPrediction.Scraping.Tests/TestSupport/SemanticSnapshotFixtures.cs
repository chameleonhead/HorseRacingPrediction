using HorseRacingPrediction.Scraping.Browser.Snapshots;

namespace HorseRacingPrediction.Scraping.Tests.TestSupport;

internal sealed record TestFragment(
    string TagName,
    IReadOnlyList<string> ClassTokens,
    string Text,
    string? Href = null);

internal sealed record TestPageLink(string Url, string Title, string Region = "content")
{
    public static implicit operator HorseRacingPrediction.Scraping.Browser.PageLinkSnapshot(TestPageLink link)
        => new(link.Url, link.Title, link.Region);
}
internal sealed record TestPageAction(string Text, string Kind);

internal sealed record TestPageCell(string Text, IReadOnlyList<TestFragment> Fragments)
{
    public TestPageCell(string text) : this(text, []) { }
}

internal sealed record TestPageTable(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<IReadOnlyList<TestPageCell>>? Cells = null);

internal sealed class TestPageSection
{
    public TestPageSection(
        string title,
        string mainText,
        List<TestPageLink> links,
        List<TestPageAction> actions,
        List<TestPageTable> tables,
        List<string> headings,
        List<HorseRacingPrediction.Scraping.Browser.PageFormSnapshot>? forms = null,
        List<HorseRacingPrediction.Scraping.Browser.PageImageSnapshot>? images = null)
    {
        Title = title;
        MainText = mainText;
        Links = links;
        Actions = actions;
        Tables = tables;
        Headings = headings;
        Forms = forms;
        Images = images;
    }

    public string Title { get; }
    public string MainText { get; }
    public List<TestPageLink> Links { get; }
    public List<TestPageAction> Actions { get; }
    public List<TestPageTable> Tables { get; }
    public List<string> Headings { get; }
    public List<HorseRacingPrediction.Scraping.Browser.PageFormSnapshot>? Forms { get; }
    public List<HorseRacingPrediction.Scraping.Browser.PageImageSnapshot>? Images { get; }
}

internal sealed record TestPageSnapshot(string Url, string Title, List<TestPageSection> Sections)
{
    public static implicit operator PageSnapshot(TestPageSnapshot fixture)
    {
        var content = new List<PageContentNode>();
        foreach (var section in fixture.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Title) && section.Headings.Count == 0)
            {
                content.Add(new PageContentNode { Kind = PageContentKind.Heading, Text = section.Title });
            }
            content.AddRange(section.Headings.Select(heading => new PageContentNode
            {
                Kind = PageContentKind.Heading,
                Text = heading,
            }));
            if (!string.IsNullOrWhiteSpace(section.MainText))
            {
                content.Add(new PageContentNode { Kind = PageContentKind.Paragraph, Text = section.MainText });
            }
            content.AddRange(section.Actions.Select(action => new PageContentNode
            {
                Kind = PageContentKind.Button,
                Text = action.Text,
            }));
        }

        return SemanticSnapshotFactory.Create(
            string.IsNullOrWhiteSpace(fixture.Url) ? "about:blank" : fixture.Url,
            fixture.Title,
            new PageContentNode { Kind = PageContentKind.Document, Children = content },
            fixture.Sections.SelectMany(section => section.Tables).Select(ConvertTable).ToArray(),
            fixture.Sections.SelectMany(section => section.Links).Select(link => new PageLinkSnapshot
            {
                Text = link.Title,
                RawHref = link.Url,
                Url = Uri.TryCreate(link.Url, UriKind.Absolute, out var url) ? url : null,
            }).ToArray());
    }

    private static PageTableSnapshot ConvertTable(TestPageTable table)
    {
        var rows = new List<PageTableRowSnapshot>();
        if (table.Headers.Count > 0)
        {
            rows.Add(new PageTableRowSnapshot
            {
                Cells = table.Headers.Select(header => new PageTableCellSnapshot { Text = header, IsHeader = true }).ToArray(),
            });
        }

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            rows.Add(new PageTableRowSnapshot
            {
                Cells = table.Rows[rowIndex].Select((text, columnIndex) => new PageTableCellSnapshot
                {
                    Text = text,
                    Fragments = table.Cells is not null && rowIndex < table.Cells.Count && columnIndex < table.Cells[rowIndex].Count
                        ? table.Cells[rowIndex][columnIndex].Fragments.Select(ConvertFragment).ToArray()
                        : [],
                }).ToArray(),
            });
        }

        return new PageTableSnapshot { Rows = rows };
    }

    private static PageElementFragmentSnapshot ConvertFragment(TestFragment fragment)
        => new()
        {
            TagName = fragment.TagName,
            Text = fragment.Text,
            ClassTokens = fragment.ClassTokens,
            RawUrl = fragment.Href,
            Url = Uri.TryCreate(fragment.Href, UriKind.Absolute, out var url) ? url : null,
        };
}
