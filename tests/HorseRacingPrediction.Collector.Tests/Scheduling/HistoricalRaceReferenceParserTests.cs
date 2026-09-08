using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Scraping.Browser.Snapshots;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class HistoricalRaceReferenceParserTests
{
    [TestMethod]
    public void Parse_WhenPastPerformanceTableExists_ReturnsDistinctRaceReferences()
    {
        var snapshot = Snapshot(["年月日", "開催", "R", "レース名"],
            [
                ["2026.04.13", "3中山8", "11R", "皐月賞"],
                ["2026.03.02", "2中山4", "9R", "弥生賞"],
                ["2026.04.13", "3中山8", "11R", "皐月賞"],
            ]);

        var result = HistoricalRaceReferenceParser.Parse(snapshot, new DateOnly(2026, 5, 18));

        Assert.HasCount(2, result);
        Assert.AreEqual(new DateOnly(2026, 4, 13), result[0].RaceDate);
        Assert.AreEqual("中山", result[0].Racecourse);
        Assert.AreEqual(11, result[0].RaceNumber);
        Assert.AreEqual(new DateOnly(2026, 3, 2), result[1].RaceDate);
        Assert.AreEqual("中山", result[1].Racecourse);
        Assert.AreEqual(9, result[1].RaceNumber);
    }

    [TestMethod]
    public void Parse_WhenYearIsOmitted_InfersPreviousYearAcrossBoundary()
    {
        var result = HistoricalRaceReferenceParser.Parse(
            Snapshot(["日付", "開催", "R"], [["12/28", "5中山9", "10R"]]),
            new DateOnly(2026, 1, 5));

        Assert.HasCount(1, result);
        Assert.AreEqual(new DateOnly(2025, 12, 28), result[0].RaceDate);
        Assert.AreEqual("中山", result[0].Racecourse);
        Assert.AreEqual(10, result[0].RaceNumber);
    }

    [TestMethod]
    public void Parse_WhenDateRacecourseAndRaceNumberAreCombinedInSingleCell_ParsesReference()
    {
        var result = HistoricalRaceReferenceParser.Parse(
            Snapshot(["過去成績"], [["2026.04.13 3中山8 11R 皐月賞"], ["2026.03.02 2中山4 9R 弥生賞"]]),
            new DateOnly(2026, 5, 18));

        Assert.HasCount(2, result);
        Assert.AreEqual(new DateOnly(2026, 4, 13), result[0].RaceDate);
        Assert.AreEqual("中山", result[0].Racecourse);
        Assert.AreEqual(11, result[0].RaceNumber);
    }

    [TestMethod]
    public void Parse_WhenHeaderNamesVaryAndRaceNumberIsEmbedded_ParsesReference()
    {
        var result = HistoricalRaceReferenceParser.Parse(
            Snapshot(["開催日", "場所"], [["2026年4月13日", "中山11R"]]),
            new DateOnly(2026, 5, 18));

        Assert.HasCount(1, result);
        Assert.AreEqual(new DateOnly(2026, 4, 13), result[0].RaceDate);
        Assert.AreEqual("中山", result[0].Racecourse);
        Assert.AreEqual(11, result[0].RaceNumber);
    }

    private static PageSnapshot Snapshot(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
        => new()
        {
            Url = new Uri("https://example.test/race"),
            Title = "race",
            Root = new PageContentNode { Kind = PageContentKind.Document },
            Metadata = new PageMetadataSnapshot { Meta = new Dictionary<string, string>(), JsonLd = [] },
            KeyValues = [],
            Tables =
            [
                new PageTableSnapshot
                {
                    Rows =
                    [
                        new PageTableRowSnapshot
                        {
                            Cells = headers.Select(header => new PageTableCellSnapshot { Text = header, IsHeader = true }).ToArray(),
                        },
                        .. rows.Select(row => new PageTableRowSnapshot
                        {
                            Cells = row.Select(text => new PageTableCellSnapshot { Text = text }).ToArray(),
                        }),
                    ],
                },
            ],
            Links = [],
            Images = [],
            Forms = [],
            Diagnostics = [],
        };
}
