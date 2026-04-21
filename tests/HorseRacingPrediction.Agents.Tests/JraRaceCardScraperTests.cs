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
