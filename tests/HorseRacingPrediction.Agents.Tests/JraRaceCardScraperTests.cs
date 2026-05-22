using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Scrapers.Jra;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// JraRaceCardScraper のユニットテスト。
/// FakeWebBrowser を使用してネットワーク依存を排除する。
/// </summary>
[TestClass]
public class JraRaceCardScraperTests
{
    private JraRaceCardScraper _sut = null!;
    private FakeWebBrowser _fakeWebBrowser = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeWebBrowser = new FakeWebBrowser();
        _sut = new JraRaceCardScraper(_fakeWebBrowser);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _fakeWebBrowser.DisposeAsync();
    }

    // ------------------------------------------------------------------ //
    // ScrapeAsync — URL
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task ScrapeAsync_ReturnsResultWithRequestedUrl()
    {
        var url = "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01sde0203_202504200501201";
        var result = await _sut.ScrapeAsync(url);

        Assert.IsNotNull(result);
        Assert.AreEqual(url, result.Url);
    }

    // ------------------------------------------------------------------ //
    // ScrapeAsync — テーブルからエントリを解析
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task ScrapeAsync_WithRaceCardTable_ParsesEntries()
    {
        _fakeWebBrowser.Snapshot = CreateSnapshotWithTable(
            headers: ["枠番", "馬番", "馬名", "性齢", "斤量", "騎手", "厩舎", "馬体重"],
            rows:
            [
                ["1", "1", "アオサギ", "牡3", "56.0", "川田将雅", "友道康夫", "480(+2)"],
                ["1", "2", "イチゴ", "牝4", "55.0", "戸崎圭太", "木村哲也", "470(-1)"],
            ]);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(2, result.Entries, "出走馬が2頭解析されること");

        var first = result.Entries[0];
        Assert.AreEqual(1, first.HorseNumber);
        Assert.AreEqual(1, first.GateNumber);
        Assert.AreEqual("アオサギ", first.HorseName);
        Assert.AreEqual("牡3", first.SexAge);
        Assert.AreEqual(56.0m, first.Weight);
        Assert.AreEqual("川田将雅", first.JockeyName);
        Assert.AreEqual("友道康夫", first.TrainerName);
        Assert.AreEqual(480m, first.BodyWeight);
        Assert.AreEqual(2m, first.BodyWeightDiff);

        var second = result.Entries[1];
        Assert.AreEqual(2, second.HorseNumber);
        Assert.AreEqual("イチゴ", second.HorseName);
        Assert.AreEqual(-1m, second.BodyWeightDiff);
    }

    [TestMethod]
    public async Task ScrapeAsync_WithoutGateNumber_ParsesEntries()
    {
        _fakeWebBrowser.Snapshot = CreateSnapshotWithTable(
            headers: ["馬番", "馬名", "騎手"],
            rows:
            [
                ["3", "ウメ", "武豊"],
            ]);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(3, result.Entries[0].HorseNumber);
        Assert.IsNull(result.Entries[0].GateNumber, "枠番カラムがない場合は null");
        Assert.AreEqual("ウメ", result.Entries[0].HorseName);
    }

    [TestMethod]
    public async Task ScrapeAsync_WithUndecidedThursdayRaceCard_UsesSequentialHorseNumbers()
    {
        _fakeWebBrowser.Snapshot = CreateSnapshotWithTable(
            headers: ["枠", "馬番", "馬名 調教師名 血統", "性齢/毛色 負担重量 騎手名", "前走"],
            rows:
            [
                ["枠", "馬番", "馬名 調教師名 血統", "性齢/毛色 負担重量 騎手名", "前走"],
                ["", "", "アラベラ 上村 洋行(栗東) 父：ロードカナロア 母：イサベル (母の父：ディープインパクト)", "牝4/黒鹿 56.0kg 北村 友一", "2026年4月26日 京都 センテニアル 3勝ク 17着"],
                ["", "ブリンカー着用", "アレナリア 藤野 健太(栗東) 父：ブラックタイド 母：リトルビスケット (母の父：タニノギムレット)", "牝7/青鹿 56.0kg 鮫島 克駿", "2026年4月12日 阪神 京橋S 3勝ク 8着"],
                ["", "", "エルフストラック 中村 直也(栗東) 父：カリフォルニアクローム 母：スペルオンミー (母の父：ダイワメジャー)", "牝5/黒鹿 56.0kg 坂井 瑠星", "2026年5月2日 新潟 三条S 牝3勝ク 2着"],
            ]);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(3, result.Entries);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, result.Entries.Select(x => x.HorseNumber).ToArray());
        CollectionAssert.AreEqual(new[] { "アラベラ", "アレナリア", "エルフストラック" }, result.Entries.Select(x => x.HorseName).ToArray());
        Assert.IsNull(result.Entries[0].GateNumber);
        Assert.AreEqual("北村 友一", result.Entries[0].JockeyName);
        Assert.AreEqual("上村 洋行", result.Entries[0].TrainerName);
        Assert.IsNull(result.Entries[0].OwnerName);
        Assert.IsNull(result.Entries[0].BreederName);
    }

    [TestMethod]
    public async Task ScrapeAsync_WithCombinedHorseCell_ParsesOwnerAndBodyWeight()
    {
        _fakeWebBrowser.Snapshot = CreateSnapshotWithTable(
            headers: ["枠番", "馬番", "馬名", "性齢/毛色 負担重量 騎手"],
            rows:
            [
                [
                    "1",
                    "1",
                    "アラビアンジョイ 6.3 (3番人気) 440kg(+8) 飯田 良枝 千代田牧場 高橋 一哉(栗東) 父：サトノアラジン 母：ブリングミージョイ (母の父：ドゥラメンテ)",
                    "牝3/鹿 55.0kg 松山 弘平"
                ],
            ]);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(1, result.Entries);
        var entry = result.Entries[0];
        Assert.AreEqual("アラビアンジョイ", entry.HorseName);
        Assert.AreEqual("松山 弘平", entry.JockeyName);
        Assert.AreEqual(55.0m, entry.Weight);
        Assert.AreEqual("牝3", entry.SexAge);
        Assert.AreEqual(440m, entry.BodyWeight);
        Assert.AreEqual(8m, entry.BodyWeightDiff);
        Assert.AreEqual("高橋 一哉", entry.TrainerName);
        Assert.AreEqual("飯田 良枝", entry.OwnerName);
        Assert.AreEqual("千代田牧場", entry.BreederName);
    }

    [TestMethod]
    public async Task ScrapeAsync_WithCombinedHorseCell_ParsesHumanBreederAndOwnerSeparately()
    {
        _fakeWebBrowser.Snapshot = CreateSnapshotWithTable(
            headers: ["枠番", "馬番", "馬名", "性齢/毛色 負担重量 騎手"],
            rows:
            [
                [
                    "4",
                    "4",
                    "ネビーイーム 1.7 (1番人気) 538kg(-2) 前田 幸貴 木村 秀則 中竹 和也(栗東) 父：キズナ 母：ヴェルヴェットクイーン (母の父：Singspiel)",
                    "牡8/黒鹿 60.0kg 小牧 加矢太"
                ],
            ]);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(1, result.Entries);
        var entry = result.Entries[0];
        Assert.AreEqual("前田 幸貴", entry.OwnerName);
        Assert.AreEqual("木村 秀則", entry.BreederName);
        Assert.AreEqual("中竹 和也", entry.TrainerName);
    }

    [TestMethod]
    public async Task ScrapeAsync_WithoutTables_ReturnsEmptyEntries()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "出馬表 | JRA",
            MainText: "ページ本文",
            Headings: [],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(0, result.Entries, "テーブルがない場合はエントリが空");
    }

    [TestMethod]
    public async Task ScrapeAsync_OnParameterErrorPage_ReturnsNull()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/error/error013.html",
            Title: "パラメータエラー JRA",
            MainText: "アクセスしたページは表示できません。",
            Headings: ["パラメータエラー"],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/JRADB/accessD.html?CNAME=invalid");

        Assert.IsNull(result, "既知のエラーページは出馬表として解析しないこと");
    }

    [TestMethod]
    public async Task ScrapeAsync_SkipsRowsWithInvalidHorseNumber()
    {
        _fakeWebBrowser.Snapshot = CreateSnapshotWithTable(
            headers: ["馬番", "馬名", "騎手"],
            rows:
            [
                ["", "空白馬番の馬", "騎手A"],
                ["abc", "文字列馬番の馬", "騎手B"],
                ["4", "エノキ", "騎手C"],
            ]);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(1, result.Entries, "有効な馬番を持つ行だけが解析されること");
        Assert.AreEqual(4, result.Entries[0].HorseNumber);
    }

    [TestMethod]
    public async Task ScrapeAsync_SkipsRowsWithBlankHorseName()
    {
        _fakeWebBrowser.Snapshot = CreateSnapshotWithTable(
            headers: ["馬番", "馬名", "騎手"],
            rows:
            [
                ["5", "", "騎手A"],
                ["6", "オルフェーヴル", "騎手B"],
            ]);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(1, result.Entries, "馬名が空の行はスキップされること");
        Assert.AreEqual("オルフェーヴル", result.Entries[0].HorseName);
    }

    // ------------------------------------------------------------------ //
    // ScrapeAsync — メタデータの解析
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task ScrapeAsync_ExtractsRaceNameFromHeadings()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "JRA",
            MainText: string.Empty,
            Headings: ["2025年4月20日 東京 11R", "天皇賞（春）", "芝・右 3200m"],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.AreEqual("天皇賞（春）", result.RaceName, "レース名が見出しから抽出されること");
    }

    [TestMethod]
    public async Task ScrapeAsync_DoesNotTreatNonSelectedInfoHeadingAsRaceName()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "出馬表 JRA",
            MainText: string.Join(
                Environment.NewLine,
                [
                    "2026年5月23日（土曜） 3回京都9日 発走時刻：15時05分",
                    "10レース",
                    "4歳以上 3勝クラス（混合）［指定］ 定量 コース：2,000メートル（芝・右）",
                    "非当選・非抽選馬情報"
                ]),
            Headings:
            [
                "JRA 日本中央競馬会",
                "検索ウィンドウ",
                "出馬表 2026年5月23日（土曜）3回京都9日 10レース",
                "シドニートロフィー",
                "コースレコード",
                "非当選・非抽選馬情報",
                "非当選馬",
                "非抽選馬",
                "JRAからのお知らせ"
            ],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.AreEqual("シドニートロフィー", result.RaceName);
    }

    [TestMethod]
    public async Task ScrapeAsync_ExtractsRacecourseFromHeadings()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "JRA",
            MainText: "2025年5月3日 京都 11R",
            Headings: ["2025年5月3日 京都 11R", "天皇賞（春）"],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.AreEqual("京都", result.Racecourse);
    }

    [TestMethod]
    public async Task ScrapeAsync_ExtractsDateFromText()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "JRA",
            MainText: "2025年4月20日 東京 11R",
            Headings: [],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.AreEqual(new DateOnly(2025, 4, 20), result.RaceDate);
    }

    [TestMethod]
    public async Task ScrapeAsync_ExtractsRaceNumberFromText()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "JRA",
            MainText: "東京 11R 天皇賞（秋）",
            Headings: [],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.AreEqual(11, result.RaceNumber);
    }

    [TestMethod]
    public async Task ScrapeAsync_ExtractsCourseTypeAndDistance()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "JRA",
            MainText: string.Empty,
            Headings: ["芝・右 2000m"],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.AreEqual("芝", result.CourseType);
        Assert.AreEqual(2000, result.Distance);
    }

    [TestMethod]
    public async Task ScrapeAsync_ExtractsDirtCourseType()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "JRA",
            MainText: "ダート・左 1600m",
            Headings: [],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.AreEqual("ダート", result.CourseType);
        Assert.AreEqual(1600, result.Distance);
    }

    [TestMethod]
    public async Task ScrapeAsync_ExtractsRaceOverviewFromRaceCardSummary()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "出馬表2026年5月16日（土曜）3回京都7日 3レース",
            MainText: string.Join(
                Environment.NewLine,
                [
                    "2026年5月16日（土曜） 3回京都7日 発走時刻：10時40分",
                    "3レース",
                    "3歳未勝利",
                    "3歳 未勝利 （混合）［指定］ 馬齢 コース：1,400メートル（芝・右）",
                    "本賞金（万円） 1着590 2着240 3着150 4着89 5着59",
                ]),
            Headings:
            [
                "出馬表",
                "2026年5月16日（土曜） 3回京都7日 発走時刻：10時40分",
                "3レース",
                "3歳未勝利",
            ],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.AreEqual(new DateOnly(2026, 5, 16), result.RaceDate);
        Assert.AreEqual("京都", result.Racecourse);
        Assert.AreEqual(3, result.MeetingNumber);
        Assert.AreEqual(7, result.DayNumber);
        Assert.AreEqual(new TimeOnly(10, 40), result.PostTime);
        Assert.AreEqual(3, result.RaceNumber);
        Assert.AreEqual("3歳未勝利", result.RaceName);
        Assert.AreEqual("3歳 未勝利 （混合）［指定］ 馬齢", result.ConditionSummary);
        Assert.AreEqual("3歳", result.AgeCondition);
        Assert.AreEqual("3", result.AgeConditionCode);
        Assert.AreEqual("未勝利", result.RaceClass);
        Assert.AreEqual("maiden", result.RaceClassCode);
        Assert.AreEqual("混合", result.Eligibility);
        CollectionAssert.AreEqual(new[] { "mixed" }, result.EligibilityCodes.ToArray());
        Assert.AreEqual("指定", result.EntryCondition);
        CollectionAssert.AreEqual(new[] { "designated" }, result.EntryConditionCodes.ToArray());
        Assert.AreEqual("馬齢", result.WeightCondition);
        Assert.AreEqual("age-weight", result.WeightConditionCode);
        Assert.AreEqual("芝", result.CourseType);
        Assert.AreEqual("右", result.TrackDirection);
        Assert.AreEqual(1400, result.Distance);
        Assert.HasCount(5, result.PrizeMoney);
        CollectionAssert.AreEqual(Enumerable.Repeat("本賞金（万円）", 5).ToArray(), result.PrizeMoney.Select(x => x.Type).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, result.PrizeMoney.Select(x => x.FinishPosition).ToArray());
        CollectionAssert.AreEqual(new[] { 590m, 240m, 150m, 89m, 59m }, result.PrizeMoney.Select(x => x.AmountInTenThousandYen).ToArray());
    }

    [TestMethod]
    public async Task ScrapeAsync_DoesNotTreatPastPerformanceAsPrizeMoney()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "出馬表2026年5月16日（土曜）3回京都7日 3レース",
            MainText: string.Join(
                Environment.NewLine,
                [
                    "2026年5月16日（土曜） 3回京都7日 発走時刻：10時40分",
                    "3レース",
                    "3歳未勝利",
                    "3歳 未勝利 （混合）［指定］ 馬齢 コース：1,400メートル（芝・右）",
                    "本賞金（万円）",
                    "1着590 2着240 3着150 4着89 5着59",
                    "2026年5月2日 京都 未勝利 3着 18頭11番 10番人気",
                    "2026年5月2日 新潟 未勝利 2着 16頭10番 11番人気",
                    "2026年3月1日 小倉 牝未勝利 16着 16頭8番 9番人気",
                ]),
            Headings:
            [
                "出馬表",
                "2026年5月16日（土曜） 3回京都7日 発走時刻：10時40分",
                "3レース",
                "3歳未勝利",
            ],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(5, result.PrizeMoney);
        CollectionAssert.AreEqual(Enumerable.Repeat("本賞金（万円）", 5).ToArray(), result.PrizeMoney.Select(x => x.Type).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, result.PrizeMoney.Select(x => x.FinishPosition).ToArray());
        CollectionAssert.AreEqual(new[] { 590m, 240m, 150m, 89m, 59m }, result.PrizeMoney.Select(x => x.AmountInTenThousandYen).ToArray());
    }

    [TestMethod]
    public async Task ScrapeAsync_StopsPrizeMoneyParsingWhenFinishPositionGoesBackward()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "出馬表2026年5月16日（土曜）3回京都7日 3レース",
            MainText: string.Join(
                Environment.NewLine,
                [
                    "2026年5月16日（土曜） 3回京都7日 発走時刻：10時40分",
                    "3レース",
                    "3歳未勝利",
                    "3歳 未勝利 （混合）［指定］ 馬齢 コース：1,400メートル（芝・右）",
                    "本賞金（万円） 1着590 2着240 3着150 4着89 5着59 2026年5月2日 京都 未勝利 3着 18頭11番 10番人気 2着 16頭10番 11番人気",
                ]),
            Headings:
            [
                "出馬表",
                "2026年5月16日（土曜） 3回京都7日 発走時刻：10時40分",
                "3レース",
                "3歳未勝利",
            ],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(5, result.PrizeMoney);
        CollectionAssert.AreEqual(Enumerable.Repeat("本賞金（万円）", 5).ToArray(), result.PrizeMoney.Select(x => x.Type).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, result.PrizeMoney.Select(x => x.FinishPosition).ToArray());
        CollectionAssert.AreEqual(new[] { 590m, 240m, 150m, 89m, 59m }, result.PrizeMoney.Select(x => x.AmountInTenThousandYen).ToArray());
    }

    [TestMethod]
    public async Task ScrapeAsync_ExtractsPrizeMoneyTypesFromHeadings()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "出馬表2026年5月16日（土曜）3回京都7日 8レース",
            MainText: string.Join(
                Environment.NewLine,
                [
                    "2026年5月16日（土曜） 3回京都7日 発走時刻：13時50分",
                    "8レース",
                    "京都ハイジャンプ",
                    "障害4歳以上 オープン 別定 コース：3,930メートル（芝・右）",
                    "本賞金（万円） 1着4,100 2着1,600 3着1,000 4着620 5着410",
                    "付加賞（万円） 1着56.7 2着16.2 3着8.1",
                ]),
            Headings:
            [
                "出馬表",
                "2026年5月16日（土曜） 3回京都7日 発走時刻：13時50分",
                "8レース",
                "京都ハイジャンプ",
            ],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(8, result.PrizeMoney);
        CollectionAssert.AreEqual(
            new[] { "本賞金（万円）", "本賞金（万円）", "本賞金（万円）", "本賞金（万円）", "本賞金（万円）", "付加賞（万円）", "付加賞（万円）", "付加賞（万円）" },
            result.PrizeMoney.Select(x => x.Type).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 1, 2, 3 }, result.PrizeMoney.Select(x => x.FinishPosition).ToArray());
        CollectionAssert.AreEqual(new[] { 4100m, 1600m, 1000m, 620m, 410m, 56.7m, 16.2m, 8.1m }, result.PrizeMoney.Select(x => x.AmountInTenThousandYen).ToArray());
    }

    [TestMethod]
    public async Task ScrapeAsync_WithFlattenedMainText_ExtractsPrizeMoneyAndNormalizedConditions()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "出馬表 JRA",
            MainText: "本文へ移動する 出馬表 2026年5月23日（土曜）3回京都9日 10レース 2026年5月23日（土曜） 3回京都9日 発走時刻：15時05分 シドニートロフィー 4歳以上 3勝クラス （混合）牝（特指） 定量 コース：2,000メートル（芝・右） 本賞金（万円） 1着1,870 2着750 3着470 4着280 5着187 印刷用ページ 馬柱の見方",
            Headings:
            [
                "JRA 日本中央競馬会",
                "検索ウィンドウ",
                "出馬表 2026年5月23日（土曜）3回京都9日 10レース",
                "シドニートロフィー",
                "コースレコード",
                "非当選・非抽選馬情報",
                "JRAからのお知らせ"
            ],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.AreEqual("シドニートロフィー", result.RaceName);
        Assert.AreEqual("4歳以上 3勝クラス （混合）牝（特指） 定量", result.ConditionSummary);
        Assert.AreEqual("4歳以上", result.AgeCondition);
        Assert.AreEqual("4up", result.AgeConditionCode);
        Assert.AreEqual("3勝クラス", result.RaceClass);
        Assert.AreEqual("3-win", result.RaceClassCode);
        Assert.AreEqual("混合 牝", result.Eligibility);
        CollectionAssert.AreEqual(new[] { "mixed", "fillies" }, result.EligibilityCodes.ToArray());
        Assert.AreEqual("特指", result.EntryCondition);
        CollectionAssert.AreEqual(new[] { "special-designated" }, result.EntryConditionCodes.ToArray());
        Assert.AreEqual("定量", result.WeightCondition);
        Assert.AreEqual("set-weight", result.WeightConditionCode);
        Assert.HasCount(5, result.PrizeMoney);
        CollectionAssert.AreEqual(Enumerable.Repeat("本賞金（万円）", 5).ToArray(), result.PrizeMoney.Select(x => x.Type).ToArray());
        CollectionAssert.AreEqual(new[] { 1870m, 750m, 470m, 280m, 187m }, result.PrizeMoney.Select(x => x.AmountInTenThousandYen).ToArray());
    }

    [TestMethod]
    [DataRow("GⅠ", "GⅠ")]
    [DataRow("GⅡ", "GⅡ")]
    [DataRow("GⅢ", "GⅢ")]
    [DataRow("重賞", "重賞")]
    public async Task ScrapeAsync_ExtractsGrade(string gradeText, string expectedGrade)
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: $"天皇賞 {gradeText}",
            MainText: string.Empty,
            Headings: [],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedGrade, result.Grade);
    }

    // ------------------------------------------------------------------ //
    // Fake implementations
    // ------------------------------------------------------------------ //

    private static PageSnapshot CreateSnapshotWithTable(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        string url = "https://www.jra.go.jp/test",
        string title = "出馬表 | JRA")
    {
        var table = new PageTableSnapshot(headers, rows);
        return new PageSnapshot(
            Url: url,
            Title: title,
            MainText: string.Empty,
            Headings: [],
            Links: [],
            Actions: [],
            Tables: [table]);
    }

    private sealed class FakeWebBrowser : IWebBrowser
    {
        public PageSnapshot? Snapshot { get; set; }

        public string? CurrentUrl => "https://www.jra.go.jp/test";

        public Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<PageSnapshot> GetPageSnapshotAsync(
            int maxLinks = 0,
            CancellationToken cancellationToken = default)
        {
            var snapshot = Snapshot ?? new PageSnapshot(
                Url: CurrentUrl ?? string.Empty,
                Title: null,
                MainText: string.Empty,
                Headings: [],
                Links: [],
                Actions: [],
                Tables: []);
            return Task.FromResult(snapshot);
        }

        public Task<string> ClickAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> SelectOptionAsync(
            string fieldText,
            string optionText,
            CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> ClickActionInSectionAsync(
            string sectionText,
            string actionText,
            CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> GetPageContentAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<IReadOnlyList<SearchResultLink>> GetLinksAsync(
            int maxResults = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SearchResultLink>>([]);

        public Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> GoBackAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
