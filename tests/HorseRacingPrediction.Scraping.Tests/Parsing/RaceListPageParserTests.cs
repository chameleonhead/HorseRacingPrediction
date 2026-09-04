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

    // 「レース結果 レース選択」ページ（過去レース結果検索→開催選択を経由した際に
    // 到達する一覧ページ）は、出馬表側の一覧ページと異なり「R」「レース番号」列を
    // 持たず、「レース結果」列のセル値（「1レース」「2レース」...）にレース番号が
    // 入る形式。実サイトE2E調査（JraNavigationRegressionE2ETests）でこの形式に
    // 遭遇し、RaceListPageParser/RaceResultPageParserいずれもCanParseがfalseになり
    // JraPageKind.Unknownになる不具合が判明したため、この形式も解析できるようにした。
    private static PageSnapshot BuildRaceResultSelectionSnapshot()
    {
        var table = new PageTableSnapshot(
            Headers: ["レース結果", "レース名", "レース映像", "距離", "馬場", "出走頭数", "最終 オッズ", "WIN5"],
            Rows:
            [
                ["レース結果", "レース名", "レース映像", "距離", "馬場", "出走頭数", "最終 オッズ", "WIN5"],
                ["1レース", "2歳未勝利牝［指定］", "PLAY", "1,400 メートル", "芝", "11頭", "1レースオッズ", ""],
                ["7レース", "関屋記念 3歳以上オープン（国際）（特指）", "PLAY", "1,600 メートル", "芝", "14頭", "7レースオッズ", "ウインファイヴ 5レース目"],
            ]);

        var section = new PageSectionSnapshot(
            title: "レース結果 レース選択",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            headings: ["レース結果 レース選択 2026年7月26日（日曜）2回新潟2日"]);

        return new PageSnapshot(Url, "レース結果 レース選択 JRA", [section]);
    }

    [TestMethod]
    public void CanParse_RaceResultSelectionTable_ReturnsTrue()
    {
        var parser = new RaceListPageParser();

        Assert.IsTrue(parser.CanParse(BuildRaceResultSelectionSnapshot()));
    }

    [TestMethod]
    public void Parse_RaceResultSelectionTable_ReturnsExpectedRaces()
    {
        var parser = new RaceListPageParser();

        var page = (JraRaceListPage)parser.Parse(BuildRaceResultSelectionSnapshot());

        Assert.AreEqual(new DateOnly(2026, 7, 26), page.Date);
        Assert.AreEqual(RaceCourse.Niigata, page.Course);
        Assert.AreEqual(2, page.Races.Count);

        Assert.AreEqual(1, page.Races[0].Id.Number);
        Assert.AreEqual("2歳未勝利牝［指定］", page.Races[0].Name);
        Assert.IsNull(page.Races[0].StartTime);

        Assert.AreEqual(7, page.Races[1].Id.Number);
        Assert.AreEqual("関屋記念 3歳以上オープン（国際）（特指）", page.Races[1].Name);
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
