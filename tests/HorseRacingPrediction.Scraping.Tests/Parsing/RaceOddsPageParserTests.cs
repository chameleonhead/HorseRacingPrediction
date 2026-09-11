using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class RaceOddsPageParserTests
{
    [TestMethod]
    public void Parse_ExtractsRaceIdentityAndWinOdds()
    {
        var table = new TestPageTable(["馬番", "単勝", "人気"], [["1", "2.5", "1"], ["2", "8.2", "4"]]);
        var section = new TestPageSection("オッズ", "", [], [], [table], ["2026年9月12日 東京 11R"]);
        var snapshot = new TestPageSnapshot("https://www.jra.go.jp/odds", "単勝オッズ", [section]);

        var page = (JraRaceOddsPage)new RaceOddsPageParser().Parse(snapshot);

        Assert.AreEqual(new RaceId(new(2026, 9, 12), RaceCourse.Tokyo, 11), page.RaceId);
        Assert.HasCount(2, page.Entries);
        Assert.AreEqual(2.5m, page.Entries[0].WinOdds);
        Assert.AreEqual(4, page.Entries[1].Popularity);
    }
}
