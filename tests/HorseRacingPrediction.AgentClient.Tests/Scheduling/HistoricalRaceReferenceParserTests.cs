using HorseRacingPrediction.AgentClient.Scheduling;
using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.AgentClient.Tests.Scheduling;

[TestClass]
public sealed class HistoricalRaceReferenceParserTests
{
    [TestMethod]
    public void Parse_WhenPastPerformanceTableExists_ReturnsDistinctRaceReferences()
    {
        var snapshot = new PageSnapshot(
            "https://example.test/race",
            "race",
            [
                new PageSectionSnapshot(
                    title: string.Empty,
                    mainText: string.Empty,
                    headings: [string.Empty],
                    links: Array.Empty<PageLinkSnapshot>().ToList(),
                    actions: Array.Empty<PageActionSnapshot>().ToList(),
                    tables:
                    [
                        new PageTableSnapshot(
                            ["年月日", "開催", "R", "レース名"],
                            [
                                new[] { "2026.04.13", "3中山8", "11R", "皐月賞" },
                                new[] { "2026.03.02", "2中山4", "9R", "弥生賞" },
                                new[] { "2026.04.13", "3中山8", "11R", "皐月賞" }
                            ])
                    ])
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
        var snapshot = new PageSnapshot(
            "https://example.test/race",
            "race",
            [
                new PageSectionSnapshot(
                    title: string.Empty,
                    mainText: string.Empty,
                    headings: [string.Empty],
                    links: Array.Empty<PageLinkSnapshot>().ToList(),
                    actions: Array.Empty<PageActionSnapshot>().ToList(),
                    tables:
                    [
                        new PageTableSnapshot(
                            ["日付", "開催", "R"],
                            [
                                new[] { "12/28", "5中山9", "10R" },
                            ])
                    ])
            ]);

        var result = HistoricalRaceReferenceParser.Parse(snapshot, new DateOnly(2026, 1, 5));

        Assert.HasCount(1, result);
        Assert.AreEqual(new DateOnly(2025, 12, 28), result[0].RaceDate);
        Assert.AreEqual("中山", result[0].Racecourse);
        Assert.AreEqual(10, result[0].RaceNumber);
    }

    [TestMethod]
    public void Parse_WhenDateRacecourseAndRaceNumberAreCombinedInSingleCell_ParsesReference()
    {
        var snapshot = new PageSnapshot(
            "https://example.test/race",
            "race",
            [
                new PageSectionSnapshot(
                    title: string.Empty,
                    mainText: string.Empty,
                    headings: [string.Empty],
                    links: Array.Empty<PageLinkSnapshot>().ToList(),
                    actions: Array.Empty<PageActionSnapshot>().ToList(),
                    tables:
                    [
                        new PageTableSnapshot(
                            ["過去成績"],
                            [
                                new[] { "2026.04.13 3中山8 11R 皐月賞" },
                                new[] { "2026.03.02 2中山4 9R 弥生賞" }
                            ])
                    ])
            ]);

        var result = HistoricalRaceReferenceParser.Parse(snapshot, new DateOnly(2026, 5, 18));

        Assert.HasCount(2, result);
        Assert.AreEqual(new DateOnly(2026, 4, 13), result[0].RaceDate);
        Assert.AreEqual("中山", result[0].Racecourse);
        Assert.AreEqual(11, result[0].RaceNumber);
    }

    [TestMethod]
    public void Parse_WhenHeaderNamesVaryAndRaceNumberIsEmbedded_ParsesReference()
    {
        var snapshot = new PageSnapshot(
            "https://example.test/race",
            "race",
            [
                new PageSectionSnapshot(
                    title: string.Empty,
                    mainText: string.Empty,
                    headings: [string.Empty],
                    links: Array.Empty<PageLinkSnapshot>().ToList(),
                    actions: Array.Empty<PageActionSnapshot>().ToList(),
                    tables:
                    [
                        new PageTableSnapshot(
                            ["開催日", "場所"],
                            [
                                new[] { "2026年4月13日", "中山11R" },
                            ])
                    ])
            ]);

        var result = HistoricalRaceReferenceParser.Parse(snapshot, new DateOnly(2026, 5, 18));

        Assert.HasCount(1, result);
        Assert.AreEqual(new DateOnly(2026, 4, 13), result[0].RaceDate);
        Assert.AreEqual("中山", result[0].Racecourse);
        Assert.AreEqual(11, result[0].RaceNumber);
    }
}