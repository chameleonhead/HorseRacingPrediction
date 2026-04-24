using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Scrapers.Jra;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// JraRaceResultScraper のユニットテスト。
/// FakeWebBrowser を使用してネットワーク依存を排除する。
/// </summary>
[TestClass]
public class JraRaceResultScraperTests
{
    private JraRaceResultScraper _sut = null!;
    private FakeWebBrowser _fakeWebBrowser = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeWebBrowser = new FakeWebBrowser();
        _sut = new JraRaceResultScraper(_fakeWebBrowser);
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
        var url = "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01skd0203_202504200501101";
        var result = await _sut.ScrapeAsync(url);

        Assert.IsNotNull(result);
        Assert.AreEqual(url, result.Url);
    }

    // ------------------------------------------------------------------ //
    // ScrapeAsync — 成績テーブルからエントリを解析
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task ScrapeAsync_WithResultTable_ParsesEntries()
    {
        _fakeWebBrowser.Snapshot = CreateSnapshotWithTable(
            headers: ["着順", "枠番", "馬番", "馬名", "性齢", "斤量", "騎手", "タイム", "着差", "後3F", "馬体重", "調教師"],
            rows:
            [
                ["1", "1", "1", "イクイノックス", "牡4", "58.0", "川田将雅", "1:58.0", string.Empty, "34.2", "520(0)", "木村哲也"],
                ["2", "2", "3", "リバティアイランド", "牝3", "53.0", "川田将雅", "1:58.2", "1/2", "34.5", "470(-2)", "中内田充正"],
            ]);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(2, result.Entries, "出走馬が2頭解析されること");

        var first = result.Entries[0];
        Assert.AreEqual(1, first.FinishPosition);
        Assert.AreEqual(1, first.GateNumber);
        Assert.AreEqual(1, first.HorseNumber);
        Assert.AreEqual("イクイノックス", first.HorseName);
        Assert.AreEqual("牡4", first.SexAge);
        Assert.AreEqual(58.0m, first.Weight);
        Assert.AreEqual("川田将雅", first.JockeyName);
        Assert.AreEqual("1:58.0", first.OfficialTime);
        Assert.AreEqual("34.2", first.LastThreeFurlongTime);
        Assert.AreEqual(520m, first.BodyWeight);
        Assert.AreEqual(0m, first.BodyWeightDiff);
        Assert.AreEqual("木村哲也", first.TrainerName);
        Assert.IsNull(first.AbnormalResultCode);

        var second = result.Entries[1];
        Assert.AreEqual(2, second.FinishPosition);
        Assert.AreEqual(3, second.HorseNumber);
        Assert.AreEqual("1/2", second.MarginText);
    }

    [TestMethod]
    public async Task ScrapeAsync_WithAbnormalCode_ParsesEntryWithCode()
    {
        _fakeWebBrowser.Snapshot = CreateSnapshotWithTable(
            headers: ["着順", "馬番", "馬名", "騎手"],
            rows:
            [
                ["取消", "5", "サンプル馬", "武豊"],
                ["1", "1", "アオサギ", "川田将雅"],
            ]);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(2, result.Entries);

        var cancelled = result.Entries.First(e => e.HorseNumber == 5);
        Assert.IsNotNull(cancelled.AbnormalResultCode, "異常コードが設定されること");
        Assert.IsNull(cancelled.FinishPosition, "取消馬の着順は null");
    }

    [TestMethod]
    public async Task ScrapeAsync_WithoutResultTable_ReturnsEmptyEntries()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "成績 | JRA",
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
            headers: ["着順", "馬番", "馬名"],
            rows:
            [
                ["1", "", "空白馬番の馬"],
                ["2", "abc", "文字列馬番の馬"],
                ["3", "4", "エノキ"],
            ]);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.HasCount(1, result.Entries, "有効な馬番を持つ行だけが解析されること");
        Assert.AreEqual(4, result.Entries[0].HorseNumber);
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
        Assert.AreEqual("天皇賞（春）", result.RaceName);
    }

    [TestMethod]
    public async Task ScrapeAsync_ExtractsRacecourse()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "JRA",
            MainText: "2025年10月26日 東京 11R",
            Headings: [],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.AreEqual("東京", result.Racecourse);
    }

    [TestMethod]
    public async Task ScrapeAsync_ExtractsDate()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "JRA",
            MainText: "2025年10月26日 東京 11R",
            Headings: [],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.AreEqual(new DateOnly(2025, 10, 26), result.RaceDate);
    }

    [TestMethod]
    public async Task ScrapeAsync_ExtractsRaceNumber()
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

    // ------------------------------------------------------------------ //
    // ScrapeAsync — 払い戻しデータの解析
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task ScrapeAsync_WithPayoutTable_ParsesPayouts()
    {
        var resultTable = new PageTableSnapshot(
            ["着順", "馬番", "馬名"],
            [["1", "3", "サンプル馬"]]);

        var payoutTable = new PageTableSnapshot(
            ["式別", "馬番", "払戻金"],
            [
                ["単勝", "3", "430"],
                ["複勝", "3", "200"],
                ["複勝", "1", "180"],
            ]);

        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "成績",
            MainText: string.Empty,
            Headings: [],
            Links: [],
            Actions: [],
            Tables: [resultTable, payoutTable]);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Payouts, "払い戻しデータが解析されること");
        Assert.HasCount(1, result.Payouts.WinPayouts, "単勝が1件");
        Assert.AreEqual("3", result.Payouts.WinPayouts[0].Combination);
        Assert.AreEqual(430m, result.Payouts.WinPayouts[0].Amount);
        Assert.HasCount(2, result.Payouts.PlacePayouts, "複勝が2件");
    }

    [TestMethod]
    public async Task ScrapeAsync_WithPayoutInMainText_ParsesPayouts()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "成績",
            MainText: "単勝 3 430円\n複勝 3 200円\n馬連 1-3 1250円",
            Headings: [],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Payouts, "本文テキストから払い戻しが解析されること");
        Assert.HasCount(1, result.Payouts.WinPayouts);
        Assert.AreEqual(430m, result.Payouts.WinPayouts[0].Amount);
        Assert.HasCount(1, result.Payouts.PlacePayouts);
        Assert.HasCount(1, result.Payouts.QuinellaPayouts);
    }

    [TestMethod]
    public async Task ScrapeAsync_WithNoPayoutData_ReturnsNullPayouts()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "成績",
            MainText: string.Empty,
            Headings: [],
            Links: [],
            Actions: [],
            Tables: []);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/test");

        Assert.IsNotNull(result);
        Assert.IsNull(result.Payouts, "払い戻しデータがない場合は null");
    }

    // ------------------------------------------------------------------ //
    // Fake implementations
    // ------------------------------------------------------------------ //

    private static PageSnapshot CreateSnapshotWithTable(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        string url = "https://www.jra.go.jp/test",
        string title = "成績 | JRA")
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
