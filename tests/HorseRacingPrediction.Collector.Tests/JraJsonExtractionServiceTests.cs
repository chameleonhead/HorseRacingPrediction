// JRAサイト再設計（docs/jra-scraping.md）により、対象の JraJsonExtractionService は一時的に無効化されている。
#if false
using HorseRacingPrediction.Collector.JraTesting;
using HorseRacingPrediction.Scraping.Browser;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorseRacingPrediction.Collector.Tests;

[TestClass]
public class JraJsonExtractionServiceTests
{
    [TestMethod]
    public async Task ExtractAsync_WithRaceCardUrl_ReturnsRaceCardJson()
    {
        var table = new PageTableSnapshot(
            Headers: new[] { "枠番", "馬番", "馬名", "性齢/毛色 負担重量 騎手" },
            Rows:
            [
                new[]
                {
                    "1",
                    "1",
                    "アラビアンジョイ 6.3 (3番人気) 440kg(+8) 飯田 良枝 千代田牧場 高橋 一哉(栗東) 父：サトノアラジン 母：ブリングミージョイ (母の父：ドゥラメンテ)",
                    "牝3/鹿 55.0kg 松山 弘平"
                }
            ]);

        var snapshot = new PageSnapshot(
            "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde1008202603070320260516/CB",
            "出馬表2026年5月16日（土曜）3回京都7日 3レース",
            [
                new PageSectionSnapshot(
                    title: "出馬表",
                    mainText: string.Join(
                        Environment.NewLine,
                        new[]
                        {
                            "2026年5月16日（土曜） 3回京都7日 発走時刻：10時40分",
                            "3レース",
                            "3歳未勝利",
                            "3歳 未勝利 （混合）［指定］ 馬齢 コース：1,400メートル（芝・右）",
                            "本賞金（万円） 1着590 2着240 3着150 4着89 5着59"
                        }),
                    headings: ["出馬表"],
                    links: Array.Empty<PageLinkSnapshot>().ToList(),
                    actions: Array.Empty<PageActionSnapshot>().ToList(),
                    tables: new[] { table }.ToList()),
                new PageSectionSnapshot(
                    title: "2026年5月16日（土曜） 3回京都7日 発走時刻：10時40分",
                    mainText: string.Empty,
                    headings: ["2026年5月16日（土曜） 3回京都7日 発走時刻：10時40分"],
                    links: Array.Empty<PageLinkSnapshot>().ToList(),
                    actions: Array.Empty<PageActionSnapshot>().ToList(),
                    tables: Array.Empty<PageTableSnapshot>().ToList()),
                new PageSectionSnapshot(
                    title: "3レース",
                    mainText: string.Empty,
                    headings: ["3レース"],
                    links: Array.Empty<PageLinkSnapshot>().ToList(),
                    actions: Array.Empty<PageActionSnapshot>().ToList(),
                    tables: Array.Empty<PageTableSnapshot>().ToList()),
                new PageSectionSnapshot(
                    title: "3歳未勝利",
                    mainText: string.Empty,
                    headings: ["3歳未勝利"],
                    links: Array.Empty<PageLinkSnapshot>().ToList(),
                    actions: Array.Empty<PageActionSnapshot>().ToList(),
                    tables: Array.Empty<PageTableSnapshot>().ToList()),
            ]);

        var service = new JraJsonExtractionService(
            new FakeWebBrowserSessionFactory(snapshot),
            NullLogger<JraJsonExtractionService>.Instance);

        var result = await service.ExtractAsync(snapshot.Url, includeSnapshot: false);

        Assert.AreEqual("RaceCard", result.PageKind);
        Assert.AreEqual("extractor", result.ExtractionMode);
        Assert.IsNull(result.Snapshot);
        Assert.IsNotNull(result.Data);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.StructureFingerprint));
        Assert.IsNotNull(result.ValidationIssues);
        Assert.HasCount(0, result.ValidationIssues);

        var data = result.Data;
        var raceName = GetProperty<string>(data, "RaceName");
        var racecourse = GetProperty<string>(data, "Racecourse");
        var meetingNumber = GetProperty<int?>(data, "MeetingNumber");
        var dayNumber = GetProperty<int?>(data, "DayNumber");
        var distance = GetProperty<int?>(data, "Distance");
        var entriesEnumerable = GetProperty<System.Collections.IEnumerable>(data, "Entries");
        Assert.IsNotNull(entriesEnumerable);
        var entries = entriesEnumerable.Cast<object>().ToList();
        var firstEntry = entries[0];
        var assignedWeight = GetProperty<decimal?>(firstEntry, "AssignedWeight");

        Assert.AreEqual("3歳未勝利", raceName);
        Assert.AreEqual("京都", racecourse);
        Assert.AreEqual(3, meetingNumber);
        Assert.AreEqual(7, dayNumber);
        Assert.AreEqual(1400, distance);
        Assert.AreEqual(55.0m, assignedWeight);
    }

    [TestMethod]
    public async Task ExtractAsync_WithNonJraUrl_ThrowsArgumentException()
    {
        var service = CreateService();

        await AssertThrowsArgumentExceptionAsync(
            () => service.ExtractAsync("https://example.com/", includeSnapshot: false));
    }

    [TestMethod]
    public async Task ExtractAsync_WithSpoofedJraSuffixDomain_ThrowsArgumentException()
    {
        var service = CreateService();

        await AssertThrowsArgumentExceptionAsync(
            () => service.ExtractAsync("https://jra.go.jp.evil.com/", includeSnapshot: false));
    }

    [TestMethod]
    public async Task ExtractAsync_WithEmptyUrl_ThrowsArgumentException()
    {
        var service = CreateService();

        await AssertThrowsArgumentExceptionAsync(
            () => service.ExtractAsync(string.Empty, includeSnapshot: false));
    }

    [TestMethod]
    public async Task ExtractAsync_WithRelativeUrl_ThrowsArgumentException()
    {
        var service = CreateService();

        await AssertThrowsArgumentExceptionAsync(
            () => service.ExtractAsync("/keiba/", includeSnapshot: false));
    }

    [TestMethod]
    public async Task ExtractAsync_WithNonHttpScheme_ThrowsArgumentException()
    {
        var service = CreateService();

        await AssertThrowsArgumentExceptionAsync(
            () => service.ExtractAsync("ftp://www.jra.go.jp/", includeSnapshot: false));
    }

    [TestMethod]
    public async Task ExtractAsync_WithBareJraDomain_DoesNotThrow()
    {
        var service = CreateService();

        var result = await service.ExtractAsync("https://jra.go.jp/", includeSnapshot: false);

        Assert.AreEqual("https://jra.go.jp/", result.InputUrl);
    }

    private static async Task AssertThrowsArgumentExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            Assert.Fail("ArgumentException が送出される想定でした。");
        }
        catch (ArgumentException)
        {
        }
    }

    private static JraJsonExtractionService CreateService()
    {
        var emptySnapshot = new PageSnapshot(
            string.Empty,
            string.Empty,
            [
                new PageSectionSnapshot(
                    title: string.Empty,
                    mainText: string.Empty,
                    headings: [string.Empty],
                    links: Array.Empty<PageLinkSnapshot>().ToList(),
                    actions: Array.Empty<PageActionSnapshot>().ToList(),
                    tables: Array.Empty<PageTableSnapshot>().ToList())
            ]);

        return new JraJsonExtractionService(
            new FakeWebBrowserSessionFactory(emptySnapshot),
            NullLogger<JraJsonExtractionService>.Instance);
    }

    private static T? GetProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName);
        Assert.IsNotNull(property, $"{propertyName} が見つかりません。");
        return (T?)property.GetValue(instance);
    }

    private sealed class FakeWebBrowserSessionFactory : IWebBrowserSessionFactory
    {
        private readonly PageSnapshot _snapshot;

        public FakeWebBrowserSessionFactory(PageSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<IWebBrowser> CreateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IWebBrowser>(new FakeWebBrowser(_snapshot));
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
            return Task.FromResult(_snapshot.MainText);
        }

        public Task<string> ClickAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot.MainText);

        public Task<string> SelectOptionAsync(string fieldText, string optionText, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot.MainText);

        public Task<string> ClickActionInSectionAsync(string sectionText, string actionText, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot.MainText);

        public Task<string> GetPageContentAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot.MainText);

        public Task<PageSnapshot> GetPageSnapshotAsync(int maxLinks = 0, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot);

        public Task<IReadOnlyList<PageLinkSnapshot>> GetLinksAsync(int maxResults = 0, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PageLinkSnapshot>>(_snapshot.Links);

        public Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot.MainText);

        public Task<string> GoBackAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot.MainText);
    }
}
#endif
