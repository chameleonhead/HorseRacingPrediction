using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class RaceCardPageParserTests
{
    private const string Url = "https://www.jra.go.jp/keiba/sample/racecard/";

    private static PageSnapshot BuildSnapshot()
    {
        var table = new PageTableSnapshot(
            Headers: ["枠番", "馬番", "馬名", "騎手", "斤量"],
            Rows:
            [
                ["1", "1", "テストホースA", "騎手A", "57"],
                ["1", "2", "テストホースB", "騎手B", "55.5"],
                ["2", "3", "テストホースC", "騎手C", "56"],
            ]);

        var section = new PageSectionSnapshot(
            title: "出馬表",
            mainText: "発走 15:40",
            links: [],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山 11R", "テストステークス(GⅢ)"]);

        return new PageSnapshot(Url, "2026年9月5日 中山 11R テストステークス 出馬表", [section]);
    }

    [TestMethod]
    public void CanParse_TableWithHorseColumns_ReturnsTrue()
    {
        var parser = new RaceCardPageParser();

        Assert.IsTrue(parser.CanParse(BuildSnapshot()));
    }

    [TestMethod]
    public void CanParse_NoEntryTable_ReturnsFalse()
    {
        var section = new PageSectionSnapshot(
            title: "無関係ページ",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [],
            headings: []);

        var snapshot = new PageSnapshot(Url, "無関係ページ", [section]);

        var parser = new RaceCardPageParser();

        Assert.IsFalse(parser.CanParse(snapshot));
    }

    [TestMethod]
    public void Parse_ReturnsExpectedRaceCard()
    {
        var parser = new RaceCardPageParser();

        var page = (JraRaceCardPage)parser.Parse(BuildSnapshot());

        Assert.AreEqual(new DateOnly(2026, 9, 5), page.RaceId.Date);
        Assert.AreEqual(RaceCourse.Nakayama, page.RaceId.Course);
        Assert.AreEqual(11, page.RaceId.Number);
        Assert.AreEqual(new TimeOnly(15, 40), page.StartTime);

        Assert.AreEqual(3, page.Entries.Count);

        Assert.AreEqual(1, page.Entries[0].HorseNumber);
        Assert.AreEqual("テストホースA", page.Entries[0].HorseName);
        Assert.AreEqual(1, page.Entries[0].FrameNumber);
        Assert.AreEqual("騎手A", page.Entries[0].JockeyName);
        Assert.AreEqual(57m, page.Entries[0].AssignedWeight);

        Assert.AreEqual(2, page.Entries[1].HorseNumber);
        Assert.AreEqual("テストホースB", page.Entries[1].HorseName);
        Assert.AreEqual(55.5m, page.Entries[1].AssignedWeight);

        Assert.AreEqual(3, page.Entries[2].HorseNumber);
        Assert.AreEqual("テストホースC", page.Entries[2].HorseName);
        Assert.AreEqual(2, page.Entries[2].FrameNumber);
    }
}
