using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class RaceListPageParserTests
{
    private const string Url = "https://www.jra.go.jp/keiba/sample/racelist/";

    private static TestPageSnapshot BuildSnapshot()
    {
        var table = new TestPageTable(
            Headers: ["R", "発走時刻", "レース名"],
            Rows:
            [
                ["1R", "10:10", "2歳未勝利"],
                ["11R", "15:40", "京成杯オータムハンデキャップ(GⅢ)"],
            ]);

        var section = new TestPageSection(
            title: "レース一覧",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山"]);

        return new TestPageSnapshot(Url, "2026年9月5日 中山 レース一覧", [section]);
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
    public void Parse_レース番号セルのDOMリンク_RaceCardUrlへ保持する()
    {
        var rows = new IReadOnlyList<string>[] { ["1R", "10:10", "2歳未勝利"] };
        var cells = new IReadOnlyList<TestPageCell>[]
        {
            [
                new("1R", [new("a", [], "1R 出馬表", "/JRADB/race/1")]),
                new("10:10", []),
                new("2歳未勝利", []),
            ],
        };
        var table = new TestPageTable(["R", "発走時刻", "レース名"], rows, cells);
        var section = new TestPageSection("レース一覧", "", [], [], [table], ["2026年9月5日 中山"]);

        var page = (JraRaceListPage)new RaceListPageParser().Parse(
            new TestPageSnapshot(Url, "2026年9月5日 中山 レース一覧", [section]));

        Assert.AreEqual("/JRADB/race/1", page.Races.Single().RaceCardUrl);
        Assert.IsNull(page.Races.Single().ResultUrl);
    }

    [TestMethod]
    public void Parse_複数リンクがある場合_出馬表と明示されたリンクだけを保持する()
    {
        var rows = new IReadOnlyList<string>[] { ["1R", "10:10", "2歳未勝利"] };
        var cells = new IReadOnlyList<TestPageCell>[]
        {
            [
                new("1R",
                [
                    new("a", [], "1R オッズ", "/JRADB/odds/1"),
                    new("a", [], "1R 出馬表", "/JRADB/card/1"),
                    new("a", [], "1R 結果", "/JRADB/result/1"),
                ]),
                new("10:10", []),
                new("2歳未勝利", []),
            ],
        };
        var table = new TestPageTable(["R", "発走時刻", "レース名"], rows, cells);
        var section = new TestPageSection("レース一覧", "", [], [], [table], ["2026年9月5日 中山"]);

        var page = (JraRaceListPage)new RaceListPageParser().Parse(
            new TestPageSnapshot(Url, "2026年9月5日 中山 レース一覧", [section]));

        Assert.AreEqual("/JRADB/card/1", page.Races.Single().RaceCardUrl);
    }

    [TestMethod]
    public void Parse_種類不明のリンクしかない場合_RaceCardUrlへ保持しない()
    {
        var rows = new IReadOnlyList<string>[] { ["1R", "10:10", "2歳未勝利"] };
        var cells = new IReadOnlyList<TestPageCell>[]
        {
            [
                new("1R", [new("a", [], "1R", "/JRADB/unknown/1")]),
                new("10:10", []),
                new("2歳未勝利", []),
            ],
        };
        var table = new TestPageTable(["R", "発走時刻", "レース名"], rows, cells);
        var section = new TestPageSection("レース一覧", "", [], [], [table], ["2026年9月5日 中山"]);

        var page = (JraRaceListPage)new RaceListPageParser().Parse(
            new TestPageSnapshot(Url, "2026年9月5日 中山 レース一覧", [section]));

        Assert.IsNull(page.Races.Single().RaceCardUrl);
    }

    // 「レース結果 レース選択」ページ（過去レース結果検索→開催選択を経由した際に
    // 到達する一覧ページ）は、出馬表側の一覧ページと異なり「R」「レース番号」列を
    // 持たず、「レース結果」列のセル値（「1レース」「2レース」...）にレース番号が
    // 入る形式。実サイトE2E調査（JraNavigationRegressionE2ETests）でこの形式に
    // 遭遇し、RaceListPageParser/RaceResultPageParserいずれもCanParseがfalseになり
    // JraPageKind.Unknownになる不具合が判明したため、この形式も解析できるようにした。
    private static TestPageSnapshot BuildRaceResultSelectionSnapshot()
    {
        var table = new TestPageTable(
            Headers: ["レース結果", "レース名", "レース映像", "距離", "馬場", "出走頭数", "最終 オッズ", "WIN5"],
            Rows:
            [
                ["レース結果", "レース名", "レース映像", "距離", "馬場", "出走頭数", "最終 オッズ", "WIN5"],
                ["1レース", "2歳未勝利牝［指定］", "PLAY", "1,400 メートル", "芝", "11頭", "1レースオッズ", ""],
                ["7レース", "関屋記念 3歳以上オープン（国際）（特指）", "PLAY", "1,600 メートル", "芝", "14頭", "7レースオッズ", "ウインファイヴ 5レース目"],
            ]);

        var section = new TestPageSection(
            title: "レース結果 レース選択",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            headings: ["レース結果 レース選択 2026年7月26日（日曜）2回新潟2日"]);

        return new TestPageSnapshot(Url, "レース結果 レース選択 JRA", [section]);
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
    public void Parse_RaceResultSelectionColumn_PreservesResultLinkWithoutRaceCardLabel()
    {
        var rows = new IReadOnlyList<string>[]
        {
            ["1レース", "2歳未勝利牝［指定］", "PLAY", "1,400 メートル", "芝", "11頭", "1レースオッズ", ""],
        };
        var cells = new IReadOnlyList<TestPageCell>[]
        {
            [
                new("1レース", [new("a", [], "1レース", "/JRADB/result/1")]),
                new("2歳未勝利牝［指定］", []),
                new("PLAY", []),
                new("1,400 メートル", []),
                new("芝", []),
                new("11頭", []),
                new("1レースオッズ", []),
                new("", []),
            ],
        };
        var table = new TestPageTable(
            ["レース結果", "レース名", "レース映像", "距離", "馬場", "出走頭数", "最終 オッズ", "WIN5"],
            rows,
            cells);
        var section = new TestPageSection(
            "レース結果 レース選択",
            "",
            [],
            [],
            [table],
            ["レース結果 レース選択 2026年7月26日（日曜）2回新潟2日"]);

        var page = (JraRaceListPage)new RaceListPageParser().Parse(
            new TestPageSnapshot(Url, "レース結果 レース選択 JRA", [section]));

        Assert.AreEqual("/JRADB/result/1", page.Races.Single().ResultUrl);
        Assert.IsNull(page.Races.Single().RaceCardUrl);
    }

    [TestMethod]
    public void CanParse_NoRaceTable_ReturnsFalse()
    {
        var section = new TestPageSection(
            title: "無関係ページ",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [],
            headings: []);

        var snapshot = new TestPageSnapshot(Url, "無関係ページ", [section]);

        var parser = new RaceListPageParser();

        Assert.IsFalse(parser.CanParse(snapshot));
    }
}
