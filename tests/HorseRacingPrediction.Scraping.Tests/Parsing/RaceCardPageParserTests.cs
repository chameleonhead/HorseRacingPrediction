using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class RaceCardPageParserTests
{
    private const string Url = "https://www.jra.go.jp/keiba/sample/racecard/";

    private static TestPageSnapshot BuildSnapshot()
    {
        var table = new TestPageTable(
            Headers: ["枠番", "馬番", "馬名", "騎手", "斤量"],
            Rows:
            [
                ["1", "1", "テストホースA", "騎手A", "57kg"],
                ["1", "2", "テストホースB", "騎手B", "55.5kg"],
                ["2", "3", "テストホースC", "騎手C", "56kg"],
            ]);

        var section = new TestPageSection(
            title: "出馬表",
            mainText: "発走 15:40",
            links: [],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山 11R", "テストステークス(GⅢ)"]);

        return new TestPageSnapshot(Url, "2026年9月5日 中山 11R テストステークス 出馬表", [section]);
    }

    [TestMethod]
    [DataRow("メイクデビュー中山")]
    [DataRow("メイクデビュー阪神")]
    [DataRow("東京優駿(GⅠ)")]
    public void Parse_CourseNameWithinRaceName_PreservesFullName(string name)
    {
        var snapshot = BuildSnapshot();
        snapshot.Headings.Clear();
        snapshot.Headings.AddRange(["JRA 日本中央競馬会", "レース結果2026年9月6日（日曜）4回中山2日 6レース", "中山", "4回中山2日", name, "払戻金", "勝馬の紹介", "エンジャムメント 2024年4月11日生牝2"]);
        var page = (JraRaceCardPage)new RaceCardPageParser().Parse(snapshot);
        Assert.AreEqual(name, page.RaceName);
    }

    [TestMethod]
    [DataRow("テストステークス(GⅢ)", "G3")]
    [DataRow("テスト大障害(J・GⅠ)", "JG1")]
    [DataRow("テスト記念(GII)", "G2")]
    public void Parse_ExtractsGradeFromHeading(string name, string grade)
    {
        var snapshot = BuildSnapshot();
        snapshot.Headings.Clear();
        snapshot.Headings.AddRange(["2026年9月5日 中山 11R", name]);
        var page = (JraRaceCardPage)new RaceCardPageParser().Parse(snapshot);
        Assert.AreEqual(grade, page.GradeCode);
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
        var section = new TestPageSection(
            title: "無関係ページ",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [],
            headings: []);

        var snapshot = new TestPageSnapshot(Url, "無関係ページ", [section]);

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

        Assert.AreEqual("テストステークス(GⅢ)", page.RaceName);
    }

    // 2026-09-06 実サイト確認（JRA出馬表ページ、4回中山1日1レース）で判明した実際のセル形式。
    // 馬名セルはブロック要素ごとの改行を保持した複数行テキストとして取得される
    // （馬名／オッズ(人気)／馬体重(増減)／馬主名／生産者名／調教師名(所属)／血統の順）。
    // 騎手列も同様に「性齢/毛色」「負担重量」「騎手名」が改行区切りで結合されている。
    private static TestPageSnapshot BuildRealSiteSnapshot(string weightText = "488kg(-2)")
    {
        var table = new TestPageTable(
            Headers: [
                "枠",
                "馬番",
                "馬名 / 単勝オッズ(人気)\n馬体重\n馬主名 / 生産者名 / 調教師名 / 血統",
                "性齢/毛色\n負担重量\n騎手名",
            ],
            Rows:
            [
                [
                    "",
                    "1",
                    $"バニーラビット\n10.7(4番人気)\n{weightText}\n藤田 晋\nノーザンファーム\n武 幸四郎(栗東)\n父：アドマイヤマーズ\n母：トレジャリング(母の父：Havana Gold)",
                    "牡4/栗\n60.0kg\n小牧 加矢太",
                ],
            ]);

        var section = new TestPageSection(
            title: "出馬表",
            mainText: "発走 10:05",
            links: [],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山 1レース", "障害3歳以上オープン"]);

        return new TestPageSnapshot(Url, "出馬表", [section]);
    }

    [TestMethod]
    public void Parse_初出走の馬体重_体重を取得し増減をnullにする()
    {
        var page = (JraRaceCardPage)new RaceCardPageParser().Parse(
            BuildRealSiteSnapshot("464kg(初出走)"));

        var entry = page.Entries.Single();
        Assert.AreEqual(464, entry.BodyWeight);
        Assert.IsNull(entry.BodyWeightChange);
    }

    [TestMethod]
    public void Parse_実サイト形式の馬名セル_馬主と調教師を正しく分離する()
    {
        var parser = new RaceCardPageParser();

        var page = (JraRaceCardPage)parser.Parse(BuildRealSiteSnapshot());

        Assert.HasCount(1, page.Entries);

        var entry = page.Entries[0];
        Assert.AreEqual(1, entry.HorseNumber);
        Assert.AreEqual("バニーラビット", entry.HorseName);
        Assert.AreEqual("藤田 晋", entry.OwnerName);
        Assert.AreEqual("武 幸四郎", entry.TrainerName);
        Assert.AreEqual("小牧 加矢太", entry.JockeyName);
        Assert.AreEqual(60.0m, entry.AssignedWeight);
        Assert.AreEqual(488, entry.BodyWeight);
        Assert.AreEqual(-2, entry.BodyWeightChange);
    }

    [TestMethod]
    public void Parse_DOM断片付きセル_Classに基づき馬主と馬体重を抽出する()
    {
        var rows = new IReadOnlyList<string>[]
        {
            ["", "1", "エーオーキング\n4.0\n(3番人気)\n456kg(+6)\n(株)ネクストトライ\n杵臼牧場\n久保田 貴士(美浦)", "牡3/鹿\n56.0kg\n騎手A"],
        };
        var cells = new IReadOnlyList<TestPageCell>[]
        {
            [
                new("", []),
                new("1", []),
                new(rows[0][2],
                [
                    new("div", ["name"], "エーオーキング"),
                    new("span", ["num"], "4.0"),
                    new("span", ["pop_rank"], "(3番人気)"),
                    new("div", ["cell", "weight"], "456kg(+6)"),
                    new("span", ["transition"], "(+6)"),
                    new("p", ["owner"], "(株)ネクストトライ"),
                    new("p", ["breeder"], "杵臼牧場"),
                    new("p", ["trainer"], "久保田 貴士(美浦)"),
                ]),
                new(rows[0][3], []),
            ],
        };
        var table = new TestPageTable(
            ["枠", "馬番", "馬名 / 単勝オッズ(人気)\n馬体重\n馬主名 / 生産者名 / 調教師名 / 血統", "性齢/毛色\n負担重量\n騎手名"],
            rows,
            cells);
        var section = new TestPageSection("出馬表", "発走 10:05", [], [], [table],
            ["2026年9月5日 中山 1レース", "テストレース"]);

        var page = (JraRaceCardPage)new RaceCardPageParser().Parse(new TestPageSnapshot(Url, "出馬表", [section]));

        Assert.HasCount(1, page.Entries);
        var entry = page.Entries[0];
        Assert.AreEqual("エーオーキング", entry.HorseName);
        Assert.AreEqual("(株)ネクストトライ", entry.OwnerName);
        Assert.AreEqual("久保田 貴士", entry.TrainerName);
        Assert.AreEqual(456, entry.BodyWeight);
        Assert.AreEqual(6, entry.BodyWeightChange);
    }

    // 実サイト確認で判明: 全ページ共通ヘッダーの<h1>はロゴ画像のみで構成されており、
    // Playwright側のテキスト抽出がimg alt「JRA 日本中央競馬会」にフォールバックするため、
    // ページ内の見出し一覧（Headings）の先頭付近にこの文字列が本文の見出しより先に混入する。
    // ParseRaceNameがこれをレース名として誤採用しないことを固定Fixtureで検証する。
    private static TestPageSnapshot BuildSnapshotWithHeaderLogoHeading()
    {
        var table = new TestPageTable(
            Headers: ["枠番", "馬番", "馬名", "騎手", "斤量"],
            Rows:
            [
                ["1", "1", "テストホースA", "騎手A", "57"],
                ["1", "2", "テストホースB", "騎手B", "55.5"],
                ["2", "3", "テストホースC", "騎手C", "56"],
            ]);

        var section = new TestPageSection(
            title: "出馬表",
            mainText: "発走 15:40",
            links: [],
            actions: [],
            tables: [table],
            // サイト共通ヘッダーの<h1>（ロゴのimg alt由来）がDOM順で本文見出しより先に来る。
            headings: ["JRA 日本中央競馬会", "2026年9月5日 中山 11R", "テストステークス(GⅢ)"]);

        return new TestPageSnapshot(Url, "2026年9月5日 中山 11R テストステークス 出馬表", [section]);
    }

    [TestMethod]
    public void Parse_HeaderLogoHeadingPresent_DoesNotUseLogoTextAsRaceName()
    {
        var parser = new RaceCardPageParser();

        var page = (JraRaceCardPage)parser.Parse(BuildSnapshotWithHeaderLogoHeading());

        Assert.AreEqual("テストステークス(GⅢ)", page.RaceName);
        Assert.AreNotEqual("JRA 日本中央競馬会", page.RaceName);
    }
}
