using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class RaceListPageParserTests
{
    private const string Url = "https://www.jra.go.jp/keiba/sample/racelist/";

    private static PageSnapshot BuildSnapshot()
    {
        var table = new PageTableSnapshot(
            Headers: ["R", "発走時刻", "レース名"],
            Rows:
            [
                ["1R", "10:10", "2歳未勝利"],
                ["11R", "15:40", "京成杯オータムハンデキャップ(GⅢ)"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース一覧",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山"]);

        return new PageSnapshot(Url, "2026年9月5日 中山 レース一覧", [section]);
    }

    [TestMethod]
    public void CanParse_TableWithRaceColumns_ReturnsTrue()
    {
        var parser = new RaceListPageParser();

        Assert.IsTrue(parser.CanParse(BuildSnapshot()));
    }

    [TestMethod]
    public void Parse_ReturnsExpectedRaces()
    {
        var parser = new RaceListPageParser();

        var page = (JraRaceListPage)parser.Parse(BuildSnapshot());

        Assert.AreEqual(new DateOnly(2026, 9, 5), page.Date);
        Assert.AreEqual(RaceCourse.Nakayama, page.Course);
        Assert.AreEqual(2, page.Races.Count);

        Assert.AreEqual(1, page.Races[0].Id.Number);
        Assert.AreEqual(1, page.Races[0].Number);
        Assert.AreEqual("2歳未勝利", page.Races[0].Name);
        Assert.AreEqual(new TimeOnly(10, 10), page.Races[0].StartTime);

        Assert.AreEqual(11, page.Races[1].Id.Number);
        Assert.AreEqual("京成杯オータムハンデキャップ(GⅢ)", page.Races[1].Name);
        Assert.AreEqual(new TimeOnly(15, 40), page.Races[1].StartTime);
    }

    [TestMethod]
    public void CanParse_NoRaceTable_ReturnsFalse()
    {
        var section = new PageSectionSnapshot(
            title: "無関係ページ",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [],
            headings: []);

        var snapshot = new PageSnapshot(Url, "無関係ページ", [section]);

        var parser = new RaceListPageParser();

        Assert.IsFalse(parser.CanParse(snapshot));
    }
}
