using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class RaceResultPageParserTests
{
    private const string Url = "https://www.jra.go.jp/keiba/sample/raceresult/";

    private static PageSnapshot BuildSnapshot()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "馬番", "馬名", "騎手", "タイム"],
            Rows:
            [
                ["1", "3", "テストホースC", "騎手C", "1:33.4"],
                ["2", "1", "テストホースA", "騎手A", "1:33.6"],
                ["3", "2", "テストホースB", "騎手B", "1:33.9"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山 11R", "テストステークス(GⅢ)"]);

        return new PageSnapshot(Url, "2026年9月5日 中山 11R テストステークス 結果", [section]);
    }

    [TestMethod]
    public void CanParse_TableWithResultColumns_ReturnsTrue()
    {
        var parser = new RaceResultPageParser();

        Assert.IsTrue(parser.CanParse(BuildSnapshot()));
    }

    [TestMethod]
    public void CanParse_NoResultTable_ReturnsFalse()
    {
        var section = new PageSectionSnapshot(
            title: "無関係ページ",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [],
            headings: []);

        var snapshot = new PageSnapshot(Url, "無関係ページ", [section]);

        var parser = new RaceResultPageParser();

        Assert.IsFalse(parser.CanParse(snapshot));
    }

    [TestMethod]
    public void Parse_ReturnsExpectedRaceResult()
    {
        var parser = new RaceResultPageParser();

        var page = (JraRaceResultPage)parser.Parse(BuildSnapshot());

        Assert.AreEqual(new DateOnly(2026, 9, 5), page.RaceId.Date);
        Assert.AreEqual(RaceCourse.Nakayama, page.RaceId.Course);
        Assert.AreEqual(11, page.RaceId.Number);

        Assert.AreEqual(3, page.Results.Count);

        Assert.AreEqual(1, page.Results[0].FinishPosition);
        Assert.AreEqual(3, page.Results[0].HorseNumber);
        Assert.AreEqual("テストホースC", page.Results[0].HorseName);
        Assert.AreEqual("騎手C", page.Results[0].JockeyName);
        Assert.AreEqual(new TimeSpan(0, 0, 1, 33, 400), page.Results[0].Time);

        Assert.AreEqual(2, page.Results[1].FinishPosition);
        Assert.AreEqual(1, page.Results[1].HorseNumber);
        Assert.AreEqual("テストホースA", page.Results[1].HorseName);

        Assert.AreEqual(3, page.Results[2].FinishPosition);
        Assert.AreEqual(2, page.Results[2].HorseNumber);
        Assert.AreEqual("テストホースB", page.Results[2].HorseName);
    }
}
