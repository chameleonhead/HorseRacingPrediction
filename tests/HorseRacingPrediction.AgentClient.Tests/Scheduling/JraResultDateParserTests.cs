using HorseRacingPrediction.AgentClient.Scheduling;
using HorseRacingPrediction.Agents.Browser;

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
            "5月3日 5月10日 5月10日",
            Array.Empty<string>(),
            [
                new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202605031120260503/AA", "5月3日 東京11R"),
                new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202605101120260510/AA", "5月10日 東京11R")
            ],
            Array.Empty<PageActionSnapshot>(),
            Array.Empty<PageTableSnapshot>());

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
            "2026年4月6日 2026年4月13日 2026年5月3日",
            ["2026年4月20日"],
            Array.Empty<SearchResultLink>(),
            Array.Empty<PageActionSnapshot>(),
            Array.Empty<PageTableSnapshot>());

        var sut = new JraResultDateParser();

        var result = sut.ParseMonthDates(snapshot, 2026, 4);

        Assert.HasCount(3, result);
        Assert.AreEqual(new DateOnly(2026, 4, 6), result[0]);
        Assert.AreEqual(new DateOnly(2026, 4, 13), result[1]);
        Assert.AreEqual(new DateOnly(2026, 4, 20), result[2]);
    }
}