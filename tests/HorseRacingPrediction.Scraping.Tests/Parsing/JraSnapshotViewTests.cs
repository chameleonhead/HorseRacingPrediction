using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class JraSnapshotViewTests
{
    [TestMethod]
    public void Create_ProjectsMultiRowHeadersAndSpansWithoutLosingFragments()
    {
        var nameFragment = new PageElementFragmentSnapshot
        {
            TagName = "span",
            Text = "Horse One",
            ClassTokens = ["name"],
        };
        var snapshot = Snapshot(
            Row(Cell("Horse", header: true, rowSpan: 2), Cell("Result", header: true, columnSpan: 2)),
            Row(Cell("Position", header: true), Cell("Time", header: true)),
            Row(Cell("Horse One", fragments: [nameFragment]), Cell("1"), Cell("1:32.1")));

        var view = JraSnapshotView.Create(snapshot);
        var table = view.Tables.Single();

        CollectionAssert.AreEqual(new[] { "Horse", "Result Position", "Result Time" }, table.Headers.ToArray());
        CollectionAssert.AreEqual(new[] { "Horse One", "1", "1:32.1" }, table.Rows.Single().ToArray());
        Assert.AreSame(nameFragment, table.GetCell(0, 0)!.FindByClass("name"));
    }

    [TestMethod]
    public void Create_RejectsOverlappingSpansInsteadOfShiftingColumns()
    {
        var snapshot = Snapshot(
            Row(Cell("A", header: true, rowSpan: 2), Cell("B", header: true)),
            Row(Cell("overlap", header: true, columnSpan: 2)));

        var view = JraSnapshotView.Create(snapshot);

        Assert.IsEmpty(view.Tables);
        Assert.HasCount(1, view.Diagnostics);
        Assert.AreEqual(0, view.Diagnostics[0].TableIndex);
        Assert.AreEqual("inconsistent-width", view.Diagnostics[0].Code);
        Assert.AreEqual(snapshot.Tables[0].Source, view.Diagnostics[0].Source);
    }

    [TestMethod]
    public void Create_PreservesNonAdjacentRepeatedHeaderLabels()
    {
        var snapshot = Snapshot(
            Row(Cell("Race", header: true)),
            Row(Cell("Result", header: true)),
            Row(Cell("Race", header: true)),
            Row(Cell("Winner")));

        Assert.AreEqual("Race Result Race", JraSnapshotView.Create(snapshot).Tables.Single().Headers.Single());
    }

    [TestMethod]
    public void Create_ReportsRowSpanBeyondSafetyLimit()
    {
        var snapshot = Snapshot(Row(Cell("oversized", rowSpan: 10_001)));

        var diagnostic = JraSnapshotView.Create(snapshot).Diagnostics.Single();

        Assert.AreEqual("row-limit-exceeded", diagnostic.Code);
    }

    [TestMethod]
    public void Create_ProvidesOrderedTextHeadingsLinksAndActions()
    {
        var snapshot = Snapshot(Row(Cell("Value"))) with
        {
            Root = new PageContentNode
            {
                Kind = PageContentKind.Document,
                Children =
                [
                    new PageContentNode { Kind = PageContentKind.Heading, Text = "Race card" },
                    new PageContentNode { Kind = PageContentKind.Button, Text = "Next race" },
                ],
            },
            Links =
            [
                new PageLinkSnapshot
                {
                    Text = string.Empty,
                    AccessibleName = "Results",
                    Url = new Uri("https://example.test/results"),
                },
            ],
        };

        var view = JraSnapshotView.Create(snapshot);

        Assert.AreEqual("Race card Next race", view.MainText);
        CollectionAssert.AreEqual(new[] { "Race card" }, view.Headings.ToArray());
        Assert.AreEqual("Results", view.Links.Single().Title);
        Assert.Contains("Next race", view.Actions.Select(action => action.Text));
    }

    private static PageSnapshot Snapshot(params PageTableRowSnapshot[] rows)
        => new()
        {
            Url = new Uri("https://example.test/race"),
            Title = "Race",
            Root = new PageContentNode { Kind = PageContentKind.Document },
            Metadata = new PageMetadataSnapshot { Meta = new Dictionary<string, string>(), JsonLd = [] },
            KeyValues = [],
            Tables =
            [
                new PageTableSnapshot
                {
                    Rows = rows,
                    Source = new PageSourceReference("table", null, "table"),
                },
            ],
            Links = [],
            Images = [],
            Forms = [],
            Diagnostics = [],
        };

    private static PageTableRowSnapshot Row(params PageTableCellSnapshot[] cells) => new() { Cells = cells };

    private static PageTableCellSnapshot Cell(
        string text,
        bool header = false,
        int rowSpan = 1,
        int columnSpan = 1,
        IReadOnlyList<PageElementFragmentSnapshot>? fragments = null)
        => new()
        {
            Text = text,
            IsHeader = header,
            RowSpan = rowSpan,
            ColumnSpan = columnSpan,
            Fragments = fragments ?? [],
        };
}
