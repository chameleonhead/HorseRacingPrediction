using HorseRacingPrediction.AgentClient.Scheduling;
using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.AgentClient.Tests.Scheduling;

[TestClass]
public sealed class JraResultDateParserTests
{
    [TestMethod]
    public void ParseMonthDates_WhenResultLinksContainTargetMonth_ReturnsDistinctDates()
    {
        var snapshot = new PageSnapshot(
            "https://www.jra.go.jp/keiba/thisweek/",
            "レース結果",
            [
                new PageSectionSnapshot(
                    title: string.Empty,
                    mainText: "5月3日 5月10日 5月10日",
                    links:
                    [
                        new PageLinkSnapshot("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202605031120260503/AA", "5月3日 東京11R"),
                        new PageLinkSnapshot("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202605101120260510/AA", "5月10日 東京11R")
                    ],
                    actions: Array.Empty<PageActionSnapshot>().ToList(),
                    tables: Array.Empty<PageTableSnapshot>().ToList())
            ]);

        var sut = new JraResultDateParser();

        var result = sut.ParseMonthDates(snapshot, 2026, 5);

        Assert.HasCount(2, result);
        Assert.AreEqual(new DateOnly(2026, 5, 3), result[0]);
        Assert.AreEqual(new DateOnly(2026, 5, 10), result[1]);
    }

    [TestMethod]
    public void ParseMonthDates_WhenSnapshotContainsFullDates_FiltersOtherMonths()
    {
        var snapshot = new PageSnapshot(
            "https://www.jra.go.jp/keiba/",
            "過去レース結果検索",
            [
                new PageSectionSnapshot(
                    title: "2026年4月20日",
                    mainText: "2026年4月6日 2026年4月13日 2026年5月3日",
                    links: Array.Empty<PageLinkSnapshot>().ToList(),
                    actions: Array.Empty<PageActionSnapshot>().ToList(),
                    tables: Array.Empty<PageTableSnapshot>().ToList())
            ]);

        var sut = new JraResultDateParser();

        var result = sut.ParseMonthDates(snapshot, 2026, 4);

        Assert.HasCount(3, result);
        Assert.AreEqual(new DateOnly(2026, 4, 6), result[0]);
        Assert.AreEqual(new DateOnly(2026, 4, 13), result[1]);
        Assert.AreEqual(new DateOnly(2026, 4, 20), result[2]);
    }
}