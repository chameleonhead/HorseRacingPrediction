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

    [TestMethod]
    public void CanParse_RaceCardContainingWinOddsColumn_ReturnsFalse()
    {
        var table = new TestPageTable(
            ["枠", "馬番", "馬名 / 単勝オッズ(人気)", "性齢 / 負担重量 / 騎手"],
            [["1", "1", "テストホース\n2.5(1番人気)", "牡3\n57kg\n騎手"]]);
        var section = new TestPageSection(
            "出馬表", "発走 15:40", [], [], [table], ["2026年9月13日 中山 5レース", "出馬表"]);
        var snapshot = new TestPageSnapshot(
            "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde0106202604040520260913/4F",
            "出馬表 JRA",
            [section]);

        Assert.IsFalse(new RaceOddsPageParser().CanParse(snapshot));
        Assert.IsTrue(new RaceCardPageParser().CanParse(snapshot));
    }
}
