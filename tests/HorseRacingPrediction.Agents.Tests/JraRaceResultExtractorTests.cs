// JRAサイト再設計（docs/jra-scraping.md）により、対象の JraRaceResultExtractor は一時的に無効化されている。
#if false
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.JraNavigation;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public sealed class JraRaceResultExtractorTests
{
    [TestMethod]
    public async Task ExtractAsync_ParsesMetadataAndGateNumberFromResultPage()
    {
        var table = new PageTableSnapshot(
            Headers: ["着順", "枠 番", "馬番", "馬名", "騎手", "タイム", "斤量", "性齢", "馬体重"],
            Rows:
            [
                ["1", "4枠", "8", "レジーナローズ", "菊沢 一樹", "0:56.1", "56.0", "牝6", "450(-10)"]
            ]);

        var snapshot = new PageSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1004202601011220260502/9A",
            "レース結果 JRA",
            [
            new PageSectionSnapshot(
                title: "2026年5月2日（土曜） 1回新潟1日",
                mainText: "4歳以上1勝クラス 芝 1,000メートル",
                headings: ["2026年5月2日（土曜） 1回新潟1日"],
                links: [],
                actions: [],
                tables: [table])
            ,
            new PageSectionSnapshot(
                title: "12レース",
                mainText: string.Empty,
                headings: ["12レース"],
                links: [],
                actions: [],
                tables: [])
            ,
            new PageSectionSnapshot(
                title: "4歳以上1勝クラス",
                mainText: string.Empty,
                headings: ["4歳以上1勝クラス"],
                links: [],
                actions: [],
                tables: [])
        ]);

        var browser = new FakeWebBrowser(snapshot);
        var extractor = new JraRaceResultExtractor();

        var extracted = await extractor.ExtractAsync(browser);
        Assert.IsNotNull(extracted);

        var result = extracted as JraRaceResultSummary;
        Assert.IsNotNull(result);
        Assert.AreEqual("4歳以上1勝クラス", result.RaceName);
        Assert.AreEqual(new DateOnly(2026, 5, 2), result.RaceDate);
        Assert.AreEqual("新潟", result.Racecourse);
        Assert.AreEqual(12, result.RaceNumber);
        Assert.AreEqual("1勝クラス", result.GradeCode);
        Assert.AreEqual("芝", result.SurfaceCode);
        Assert.AreEqual(1000, result.DistanceMeters);
        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(4, result.Entries[0].GateNumber);
    }

    [TestMethod]
    public async Task ExtractAsync_PrefersSelectedRaceHeading_WhenNavigationContainsOtherRacecourses()
    {
        var snapshot = new PageSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1001202601060420260809/AF",
            "レース結果 JRA",
            [
                new PageSectionSnapshot(
                    title: "レース結果 2026年8月9日（日曜）1回札幌6日 4レース",
                    mainText: "2回新潟6日 2回中京6日 1回札幌6日 3歳未勝利 コース：1,700メートル（ダート・右）",
                    headings: ["レース結果 2026年8月9日（日曜）1回札幌6日 4レース", "3歳未勝利"],
                    links: [],
                    actions: [],
                    tables: [])
            ]);

        var result = await new JraRaceResultExtractor().ExtractAsync(new FakeWebBrowser(snapshot)) as JraRaceResultSummary;

        Assert.IsNotNull(result);
        Assert.AreEqual("札幌", result.Racecourse);
        Assert.AreEqual("未勝利", result.GradeCode);
        Assert.AreEqual("ダート", result.SurfaceCode);
        Assert.AreEqual(1700, result.DistanceMeters);
    }

    private sealed class FakeWebBrowser : IWebBrowser
    {
        private readonly PageSnapshot _snapshot;

        public FakeWebBrowser(PageSnapshot snapshot)
        {
            _snapshot = snapshot;
            CurrentUrl = snapshot.Url;
        }

        public string? CurrentUrl { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default)
        {
            CurrentUrl = url;
            return Task.FromResult(string.Empty);
        }

        public Task<string> ClickAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> SelectOptionAsync(string fieldText, string optionText, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> ClickActionInSectionAsync(string sectionText, string actionText, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> GetPageContentAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<PageSnapshot> GetPageSnapshotAsync(int maxLinks = 0, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot);

        public Task<IReadOnlyList<PageLinkSnapshot>> GetLinksAsync(int maxResults = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PageLinkSnapshot>>([]);

        public Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> GoBackAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }
}
#endif
