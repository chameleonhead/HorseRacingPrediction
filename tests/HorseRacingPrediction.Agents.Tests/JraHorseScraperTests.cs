// JRAサイト再設計（docs/jra-scraping.md）により、対象の JraHorseScraper は一時的に無効化されている。
#if false
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// JraHorseScraper のユニットテスト。
/// FakeWebBrowser を使用してネットワーク依存を排除する。
/// </summary>
[TestClass]
public class JraHorseScraperTests
{
    private const string HorseProfileUrl = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=test";

    private JraHorseScraper _sut = null!;
    private FakeWebBrowser _fakeWebBrowser = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeWebBrowser = new FakeWebBrowser();
        _sut = new JraHorseScraper(_fakeWebBrowser);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _fakeWebBrowser.DisposeAsync();
    }

    [TestMethod]
    public async Task ScrapeAsync_OnNonHorseProfilePage_ReturnsNull()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            "https://www.jra.go.jp/JRADB/accessD.html?CNAME=test",
            "出馬表 | JRA",
            [
                new PageSectionSnapshot(
                    title: string.Empty,
                    mainText: string.Empty,
                    headings: [string.Empty],
                    links: [],
                    actions: [],
                    tables: [])
            ]);

        var result = await _sut.ScrapeAsync("https://www.jra.go.jp/JRADB/accessD.html?CNAME=test");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task ScrapeAsync_ParsesProfileFacts()
    {
        var factsTable = new PageTableSnapshot(
            ["項目", "内容"],
            [
                ["性別", "牡"],
                ["生年月日", "2020年2月24日"],
                ["父", "キタサンブラック"],
                ["母", "スキア"],
                ["馬主名", "社台レースホース"],
                ["生産牧場", "社台ファーム"],
                ["調教師名", "手塚貴久"],
            ]);
        _fakeWebBrowser.Snapshot = CreateSnapshot("ソールオリエンス", [factsTable]);

        var result = await _sut.ScrapeAsync(HorseProfileUrl);

        Assert.IsNotNull(result);
        Assert.AreEqual("ソールオリエンス", result.Profile.DisplayName);
        Assert.AreEqual("M", result.Profile.SexCode);
        Assert.AreEqual(new DateOnly(2020, 2, 24), result.Profile.BirthDate);
        Assert.AreEqual("キタサンブラック", result.Profile.SireName);
        Assert.AreEqual("スキア", result.Profile.DamName);
        Assert.AreEqual("社台レースホース", result.Profile.OwnerName);
        Assert.AreEqual("社台ファーム", result.Profile.BreederName);
        Assert.AreEqual("手塚貴久", result.Profile.TrainerName);
        Assert.IsEmpty(result.RaceHistory);
    }

    [TestMethod]
    public async Task ScrapeAsync_WithHistoryTable_ParsesRaceHistoryEntries()
    {
        var historyTable = new PageTableSnapshot(
            ["年月日", "競馬場", "R", "レース名", "枠番", "馬番", "着順", "騎手名", "斤量", "距離", "タイム", "着差", "上り3F", "馬体重", "勝ち馬(2着馬)", "賞金"],
            [
                ["2023年5月28日", "東京", "11", "日本ダービー", "1", "1", "1", "横山武史", "57.0", "芝2400", "2:22.5", string.Empty, "33.8", "468(+2)", "タスティエーラ", "20000"],
                ["2023年10月29日", "東京", "11", "天皇賞(秋)", "3", "6", "2", "横山武史", "58.0", "芝2000", "1:56.9", "クビ", "33.5", "470(+2)", "イクイノックス", "9000"],
            ]);
        _fakeWebBrowser.Snapshot = CreateSnapshot("ソールオリエンス", [historyTable]);

        var result = await _sut.ScrapeAsync(HorseProfileUrl);

        Assert.IsNotNull(result);
        Assert.HasCount(2, result.RaceHistory);

        var first = result.RaceHistory[0];
        Assert.AreEqual(new DateOnly(2023, 5, 28), first.RaceDate);
        Assert.AreEqual("東京", first.Racecourse);
        Assert.AreEqual(11, first.RaceNumber);
        Assert.AreEqual("日本ダービー", first.RaceName);
        Assert.AreEqual(1, first.GateNumber);
        Assert.AreEqual(1, first.HorseNumber);
        Assert.AreEqual(1, first.FinishPosition);
        Assert.IsNull(first.AbnormalResultCode);
        Assert.AreEqual("横山武史", first.JockeyName);
        Assert.AreEqual(57.0m, first.AssignedWeight);
        Assert.AreEqual("芝", first.SurfaceCode);
        Assert.AreEqual(2400, first.DistanceMeters);
        Assert.AreEqual("2:22.5", first.OfficialTime);
        Assert.AreEqual("33.8", first.LastThreeFurlongTime);
        Assert.AreEqual(468m, first.BodyWeight);
        Assert.AreEqual(2m, first.BodyWeightDiff);
        Assert.AreEqual("タスティエーラ", first.WinnerOrRunnerUpHorseName);
        Assert.AreEqual(20000m, first.PrizeMoney);

        var second = result.RaceHistory[1];
        Assert.AreEqual(2, second.FinishPosition);
        Assert.AreEqual("クビ", second.MarginText);
        Assert.AreEqual("イクイノックス", second.WinnerOrRunnerUpHorseName);
    }

    [TestMethod]
    public async Task ScrapeAsync_WithAbnormalFinishCode_ParsesAbnormalCode()
    {
        var historyTable = new PageTableSnapshot(
            ["年月日", "競馬場", "R", "レース名", "馬番", "着順"],
            [
                ["2023年1月1日", "中山", "1", "サンプルレース", "5", "中止"],
            ]);
        _fakeWebBrowser.Snapshot = CreateSnapshot("サンプルホース", [historyTable]);

        var result = await _sut.ScrapeAsync(HorseProfileUrl);

        Assert.IsNotNull(result);
        Assert.HasCount(1, result.RaceHistory);
        Assert.IsNull(result.RaceHistory[0].FinishPosition);
        Assert.AreEqual("中止", result.RaceHistory[0].AbnormalResultCode);
    }

    [TestMethod]
    public async Task ScrapeCurrentPageAsync_DoesNotNavigate()
    {
        var historyTable = new PageTableSnapshot(
            ["年月日", "競馬場", "R", "レース名", "馬番", "着順"],
            [
                ["2023年1月1日", "中山", "1", "サンプルレース", "5", "1"],
            ]);
        _fakeWebBrowser.Snapshot = CreateSnapshot("サンプルホース", [historyTable]);
        _fakeWebBrowser.SetCurrentUrl(HorseProfileUrl);

        var result = await _sut.ScrapeCurrentPageAsync();

        Assert.IsNotNull(result);
        Assert.HasCount(1, result.RaceHistory);
    }

    // ------------------------------------------------------------------ //
    // Fake implementations
    // ------------------------------------------------------------------ //

    private static PageSnapshot CreateSnapshot(
        string horseNameHeading,
        IReadOnlyList<PageTableSnapshot> tables,
        string url = HorseProfileUrl,
        string title = "競走馬情報 | JRA")
    {
        return new PageSnapshot(
            url,
            title,
            [
                new PageSectionSnapshot(
                    title: string.Empty,
                    mainText: string.Empty,
                    headings: [horseNameHeading],
                    links: [],
                    actions: [],
                    tables: tables.ToList())
            ]);
    }

    private sealed class FakeWebBrowser : IWebBrowser
    {
        public PageSnapshot? Snapshot { get; set; }

        private string? _currentUrl = HorseProfileUrl;

        public string? CurrentUrl => _currentUrl;

        public void SetCurrentUrl(string url) => _currentUrl = url;

        public Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default)
        {
            _currentUrl = url;
            return Task.FromResult(string.Empty);
        }

        public Task<PageSnapshot> GetPageSnapshotAsync(
            int maxLinks = 0,
            CancellationToken cancellationToken = default)
        {
            var snapshot = Snapshot ?? new PageSnapshot(
                CurrentUrl ?? string.Empty,
                string.Empty,
                [
                    new PageSectionSnapshot(
                        title: string.Empty,
                        mainText: string.Empty,
                        headings: [string.Empty],
                        links: [],
                        actions: [],
                        tables: [])
                ]);
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

        public Task<IReadOnlyList<PageLinkSnapshot>> GetLinksAsync(
            int maxResults = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PageLinkSnapshot>>([]);

        public Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> GoBackAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
#endif
