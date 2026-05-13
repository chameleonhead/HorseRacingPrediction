using EventFlow;
using EventFlow.Aggregates;
using EventFlow.Aggregates.ExecutionResults;
using EventFlow.Commands;
using EventFlow.Core;
using EventFlow.Queries;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using HorseRacingPrediction.Agents.Workflow;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// JraRaceResultCollectionWorkflow のユニットテスト。
/// Fake 実装を使用してネットワーク・DB・LLM への依存を排除する。
/// </summary>
[TestClass]
public class JraRaceResultCollectionWorkflowTests
{
    private JraRaceResultCollectionWorkflow _sut = null!;
    private FakeWebBrowser _fakeWebBrowser = null!;
    private FakeCommandBus _fakeCommandBus = null!;
    private CollaboratingFakeQueryProcessor _fakeQueryProcessor = null!;
    private FakeRaceQueryService _fakeRaceQueryService = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeWebBrowser = new FakeWebBrowser();
        _fakeCommandBus = new FakeCommandBus();
        _fakeQueryProcessor = new CollaboratingFakeQueryProcessor(_fakeCommandBus);
        _fakeRaceQueryService = new FakeRaceQueryService();

        var scraper = new JraRaceResultScraper(_fakeWebBrowser);
        var writeTools = new DataCollectionWriteTools(
            new EventFlowDataCollectionWriteService(_fakeCommandBus, _fakeQueryProcessor));

        _sut = new JraRaceResultCollectionWorkflow(_fakeWebBrowser, scraper, writeTools, _fakeRaceQueryService);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _fakeWebBrowser.DisposeAsync();
    }

    // ------------------------------------------------------------------ //
    // DiscoverUrlsAsync
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task DiscoverUrlsAsync_UsesRecentResultButtonsWithoutUrlGeneration()
    {
        var currentYear = DateTime.Today.Year;
        var raceDate = new DateOnly(currentYear, 5, 10);

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/keiba/",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/keiba/",
                Title: "競馬メニュー",
                MainText: string.Empty,
                Headings: [],
                Links:
                [
                    new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html", "レース結果")
                ],
                Actions: [],
                Tables: []));

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/JRADB/accessS.html",
                Title: "レース結果 開催選択",
                MainText: "5月10日（日曜） 2回東京6日",
                Headings: ["レース結果", "5月10日（日曜）"],
                Links:
                [
                    new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html?kaisai=tokyo", "2回東京6日")
                ],
                Actions: [],
                Tables: []));

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html?kaisai=tokyo",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/JRADB/accessS.html?kaisai=tokyo",
                Title: $"レース結果 レース選択 {currentYear}年5月10日（日曜）2回東京6日",
                MainText: $"{currentYear}年5月10日（日曜）2回東京6日",
                Headings: [$"{currentYear}年5月10日（日曜）2回東京6日"],
                Links:
                [
                    new SearchResultLink(
                        $"https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1005{currentYear}020611{currentYear}0510/7F",
                        string.Empty),
                    new SearchResultLink(
                        $"https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1005{currentYear}020612{currentYear}0510/34",
                        string.Empty)
                ],
                Actions: [],
                Tables: []));

        var result = await _sut.DiscoverUrlsAsync(raceDate);

        Assert.HasCount(2, result);
        Assert.IsEmpty(_fakeWebBrowser.SelectHistory, "recent result では検索フォームを使わないこと");
        Assert.AreEqual("05", result[0].RacecourseCode);
        Assert.AreEqual(raceDate, result[0].RaceDate);
        Assert.AreEqual(11, result[0].RaceNumber);
    }

    [TestMethod]
    public async Task DiscoverUrlsAsync_UsesHistoricalSearchForPastYear()
    {
        var raceDate = new DateOnly(DateTime.Today.Year - 1, 5, 11);

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/keiba/",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/keiba/",
                Title: "競馬メニュー",
                MainText: string.Empty,
                Headings: [],
                Links:
                [
                    new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html", "レース結果")
                ],
                Actions: [],
                Tables: []));

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/JRADB/accessS.html",
                Title: "レース結果 開催選択",
                MainText: string.Empty,
                Headings: ["レース結果", "過去のレース結果"],
                Links:
                [
                    new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html?search=true", "過去レース結果検索")
                ],
                Actions: [],
                Tables: []));

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html?search=true",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/JRADB/accessS.html?search=true",
                Title: "過去レース結果検索",
                MainText: "検索条件",
                Headings: ["過去レース結果検索"],
                Links:
                [
                    new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html?month=target", "検索")
                ],
                Actions: [],
                Tables: []));

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html?month=target",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/JRADB/accessS.html?month=target",
                Title: "過去レース結果検索",
                MainText: $"{raceDate.Year}年5月11日（日曜） 2回東京7日",
                Headings: [$"{raceDate.Year}年5月"],
                Links:
                [
                    new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html?kaisai=past-tokyo", "2回東京7日")
                ],
                Actions: [],
                Tables: []));

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html?kaisai=past-tokyo",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/JRADB/accessS.html?kaisai=past-tokyo",
                Title: $"レース結果 レース選択 {raceDate.Year}年5月11日（日曜）2回東京7日",
                MainText: $"{raceDate.Year}年5月11日（日曜）2回東京7日",
                Headings: [$"{raceDate.Year}年5月11日（日曜）2回東京7日"],
                Links:
                [
                    new SearchResultLink(
                        $"https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1005{raceDate.Year}020711{raceDate.Year}0511/7F",
                        string.Empty)
                ],
                Actions: [],
                Tables: []));

        var result = await _sut.DiscoverUrlsAsync(raceDate);

        Assert.HasCount(1, result);
        CollectionAssert.AreEqual(
            new[]
            {
                $"年:{raceDate.Year}",
                $"月:{raceDate.Month}"
            },
            _fakeWebBrowser.SelectHistory);
        Assert.AreEqual(11, result[0].RaceNumber);
        Assert.AreEqual(raceDate, result[0].RaceDate);
    }

    [TestMethod]
    public async Task DiscoverUrlsAsync_NoMeetingButtons_ReturnsEmptyList()
    {
        var raceDate = new DateOnly(DateTime.Today.Year - 1, 10, 26);

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/keiba/",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/keiba/",
                Title: "競馬メニュー",
                MainText: string.Empty,
                Headings: [],
                Links:
                [
                    new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html", "レース結果")
                ],
                Actions: [],
                Tables: []));

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/JRADB/accessS.html",
                Title: "レース結果 開催選択",
                MainText: string.Empty,
                Headings: ["レース結果", "過去のレース結果"],
                Links:
                [
                    new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html?search=true", "過去レース結果検索")
                ],
                Actions: [],
                Tables: []));

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html?search=true",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/JRADB/accessS.html?search=true",
                Title: "過去レース結果検索",
                MainText: "検索条件",
                Headings: ["過去レース結果検索"],
                Links:
                [
                    new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html?month=empty", "検索")
                ],
                Actions: [],
                Tables: []));

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html?month=empty",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/JRADB/accessS.html?month=empty",
                Title: "過去レース結果検索",
                MainText: $"{raceDate.Year}年10月",
                Headings: [$"{raceDate.Year}年10月"],
                Links: [],
                Actions: [],
                Tables: []));

        var result = await _sut.DiscoverUrlsAsync(raceDate);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task FilterUnregisteredUrlsAsync_SkipsRegisteredAndDuplicateRaces()
    {
        var raceDate = new DateOnly(2025, 6, 1);
        _fakeRaceQueryService.RegisteredRaces.Add(new RaceSearchSummary("race-1", raceDate, "東京", 11));

        var urls = new[]
        {
            new JraRaceResultUrl("https://example.test/1", "東京", "05", raceDate, 11),
            new JraRaceResultUrl("https://example.test/2", "東京", "05", raceDate, 12),
            new JraRaceResultUrl("https://example.test/3", "東京", "05", raceDate, 12)
        };

        var filtered = await _sut.FilterUnregisteredUrlsAsync(urls, raceDate);

        Assert.AreEqual(1, filtered.Count);
        Assert.AreEqual("https://example.test/2", filtered[0].Url);
    }

    // ------------------------------------------------------------------ //
    // ScrapeAllAsync
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task ScrapeAllAsync_WithValidUrl_ReturnsScrapedData()
    {
        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "天皇賞（秋） 成績 | JRA",
            MainText: "2025年10月26日 東京 11R 天皇賞（秋）",
            Headings: ["天皇賞（秋）", "2025年10月26日 東京 11R"],
            Links: [],
            Actions: [],
            Tables:
            [
                new PageTableSnapshot(
                    ["着順", "枠番", "馬番", "馬名", "騎手", "斤量"],
                    [
                        ["1", "1", "1", "イクイノックス", "川田将雅", "58.0"],
                        ["2", "2", "3", "リバティアイランド", "戸崎圭太", "56.0"],
                    ])
            ]);

        var urls = new[]
        {
            JraRaceResultUrl.ParseFromUrl(
                "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01skd0203_20251026051101&sub=",
                "東京")
        };

        var results = await _sut.ScrapeAllAsync(urls);

        Assert.HasCount(1, results);
        Assert.HasCount(2, results[0].Data.Entries, "出走馬が2頭解析されること");
        Assert.AreEqual("イクイノックス", results[0].Data.Entries[0].HorseName);
        Assert.AreEqual(1, results[0].Data.Entries[0].FinishPosition);
    }

    // ------------------------------------------------------------------ //
    // SaveAllAsync
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task SaveAllAsync_WithWinner_PublishesDeclareRaceResultCommand()
    {
        var url = JraRaceResultUrl.ParseFromUrl(
            "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01skd0203_20251026051101&sub=",
            "東京");

        var data = new JraRaceResultData(
            Url: url.Url,
            RaceName: "天皇賞（秋）",
            Racecourse: "東京",
            RaceDate: new DateOnly(2025, 10, 26),
            RaceNumber: 11,
            CourseType: "芝",
            Distance: 2000,
            Grade: "GⅠ",
            Entries:
            [
                new JraRaceResultEntryData(1, 1, 1, "イクイノックス", "川田将雅", 58.0m, "牡4", "1:58.0", null, "34.2", 520m, 0m, "木村哲也", null),
                new JraRaceResultEntryData(2, 3, 2, "リバティアイランド", "戸崎圭太", 56.0m, "牝3", "1:58.2", "1/2", "34.5", 470m, -2m, "中内田充正", null),
            ],
            Payouts: null);

        var (savedIds, errors) = await _sut.SaveAllAsync([(url, data)]);

        Assert.HasCount(1, savedIds, "保存されたレース ID が1件であること");
        Assert.IsEmpty(errors, "エラーがないこと");
        CollectionAssert.Contains(_fakeCommandBus.PublishedCommandNames, "CreateRaceCommand",
            "CreateRaceCommand が発行されること");
        CollectionAssert.Contains(_fakeCommandBus.PublishedCommandNames, "DeclareRaceResultCommand",
            "DeclareRaceResultCommand が発行されること");
        CollectionAssert.Contains(_fakeCommandBus.PublishedCommandNames, "DeclareEntryResultCommand",
            "DeclareEntryResultCommand が発行されること");
    }

    [TestMethod]
    public async Task SaveAllAsync_WithPayouts_PublishesDeclarePayoutResultCommand()
    {
        var url = JraRaceResultUrl.ParseFromUrl(
            "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01skd0203_20251026051101&sub=",
            "東京");

        var data = new JraRaceResultData(
            Url: url.Url,
            RaceName: "天皇賞（秋）",
            Racecourse: "東京",
            RaceDate: new DateOnly(2025, 10, 26),
            RaceNumber: 11,
            CourseType: "芝",
            Distance: 2000,
            Grade: "GⅠ",
            Entries:
            [
                new JraRaceResultEntryData(1, 1, 1, "イクイノックス", null, null, null, null, null, null, null, null, null, null),
            ],
            Payouts: new JraRacePayoutData(
                WinPayouts: [new JraPayoutEntry("1", 430)],
                PlacePayouts: [new JraPayoutEntry("1", 200)],
                QuinellaPayouts: [],
                WidePayouts: [],
                ExactaPayouts: [],
                TrioPayouts: [],
                TrifectaPayouts: []));

        var (savedIds, errors) = await _sut.SaveAllAsync([(url, data)]);

        Assert.HasCount(1, savedIds);
        Assert.IsEmpty(errors);
        CollectionAssert.Contains(_fakeCommandBus.PublishedCommandNames, "DeclarePayoutResultCommand",
            "DeclarePayoutResultCommand が発行されること");
    }

    [TestMethod]
    public async Task SaveAllAsync_NoWinner_SkipsResultDeclaration()
    {
        var url = JraRaceResultUrl.ParseFromUrl(
            "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01skd0203_20251026051101&sub=",
            "東京");

        var data = new JraRaceResultData(
            Url: url.Url,
            RaceName: "天皇賞（秋）",
            Racecourse: "東京",
            RaceDate: new DateOnly(2025, 10, 26),
            RaceNumber: 11,
            CourseType: null,
            Distance: null,
            Grade: null,
            Entries: [],  // 空のエントリ（勝ち馬なし）
            Payouts: null);

        var (savedIds, errors) = await _sut.SaveAllAsync([(url, data)]);

        // レースは作成されるが、成績宣言はされない
        Assert.HasCount(1, savedIds);
        CollectionAssert.Contains(_fakeCommandBus.PublishedCommandNames, "CreateRaceCommand");
        CollectionAssert.DoesNotContain(_fakeCommandBus.PublishedCommandNames, "DeclareRaceResultCommand",
            "勝ち馬がない場合は成績宣言されないこと");
    }

    [TestMethod]
    public async Task SaveAllAsync_MissingRaceDate_SkipsAndReportsError()
    {
        var url = new JraRaceResultUrl("https://www.jra.go.jp/test", "東京", null, null, null);
        var data = new JraRaceResultData(
            Url: url.Url,
            RaceName: "テスト",
            Racecourse: "東京",
            RaceDate: null,
            RaceNumber: 5,
            CourseType: null,
            Distance: null,
            Grade: null,
            Entries: [],
            Payouts: null);

        var (savedIds, errors) = await _sut.SaveAllAsync([(url, data)]);

        Assert.IsEmpty(savedIds);
        Assert.HasCount(1, errors, "エラーが1件報告されること");
    }

    // ------------------------------------------------------------------ //
    // CollectAsync (統合)
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task CollectAsync_EndToEnd_ReturnsPopulatedResult()
    {
        var currentYear = DateTime.Today.Year;
        var raceDate = new DateOnly(currentYear, 5, 10);
        var resultUrl = $"https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1005{currentYear}020611{currentYear}0510/7F";

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/keiba/",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/keiba/",
                Title: "競馬メニュー",
                MainText: string.Empty,
                Headings: [],
                Links:
                [
                    new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html", "レース結果")
                ],
                Actions: [],
                Tables: []));

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/JRADB/accessS.html",
                Title: "レース結果 開催選択",
                MainText: "5月10日（日曜） 2回東京6日",
                Headings: ["レース結果", "5月10日（日曜）"],
                Links:
                [
                    new SearchResultLink("https://www.jra.go.jp/JRADB/accessS.html?kaisai=tokyo", "2回東京6日")
                ],
                Actions: [],
                Tables: []));

        _fakeWebBrowser.SetSnapshot(
            "https://www.jra.go.jp/JRADB/accessS.html?kaisai=tokyo",
            new PageSnapshot(
                Url: "https://www.jra.go.jp/JRADB/accessS.html?kaisai=tokyo",
                Title: $"レース結果 レース選択 {currentYear}年5月10日（日曜）2回東京6日",
                MainText: $"{currentYear}年5月10日（日曜）2回東京6日",
                Headings: [$"{currentYear}年5月10日（日曜）2回東京6日"],
                Links:
                [
                    new SearchResultLink(resultUrl, string.Empty)
                ],
                Actions: [],
                Tables: []));

        _fakeWebBrowser.SetSnapshot(
            resultUrl,
            new PageSnapshot(
                Url: resultUrl,
                Title: "天皇賞（秋） 成績 | JRA",
                MainText: $"{currentYear}年5月10日 東京 11R 天皇賞（秋） 芝・右 2000m GⅠ",
                Headings: ["天皇賞（秋）", $"{currentYear}年5月10日 東京 11R"],
                Links: [],
                Actions: [],
                Tables:
                [
                    new PageTableSnapshot(
                        ["着順", "馬番", "馬名", "騎手"],
                        [
                            ["1", "1", "イクイノックス", "川田将雅"],
                        ])
                ]));

        var result = await _sut.CollectAsync(raceDate);

        Assert.AreEqual(raceDate, result.RaceDate);
        Assert.HasCount(1, result.DiscoveredUrls, "URL が1件発見されること");
        Assert.HasCount(1, result.ScrapedResults, "成績が1件スクレイプされること");
        Assert.HasCount(1, result.SavedRaceIds, "レースが1件保存されること");
        Assert.IsEmpty(result.Errors);
    }

    // ------------------------------------------------------------------ //
    // Fake implementations
    // ------------------------------------------------------------------ //

    private sealed class FakeWebBrowser : IWebBrowser
    {
        public PageSnapshot? Snapshot { get; set; }
        public List<string> ClickHistory { get; } = [];
        public List<string> NavigationHistory { get; } = [];
        public List<string> SelectHistory { get; } = [];

        private readonly Dictionary<string, PageSnapshot> _snapshotsByUrl = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<string> _history = new();

        public string? CurrentUrl { get; private set; } = "https://www.jra.go.jp/keiba/";

        public void SetSnapshot(string url, PageSnapshot snapshot)
        {
            _snapshotsByUrl[url] = snapshot;
        }

        public Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(CurrentUrl))
            {
                _history.Push(CurrentUrl);
            }

            CurrentUrl = url;
            NavigationHistory.Add(url);
            return Task.FromResult(string.Empty);
        }

        public Task<PageSnapshot> GetPageSnapshotAsync(
            int maxLinks = 0,
            CancellationToken cancellationToken = default)
        {
            var snapshot = GetCurrentSnapshot();
            return Task.FromResult(snapshot);
        }

        public Task<string> ClickAsync(string text, CancellationToken cancellationToken = default)
        {
            ClickHistory.Add(text);

            var snapshot = GetCurrentSnapshot();
            var matchedLink = snapshot.Links.FirstOrDefault(link =>
                !string.IsNullOrWhiteSpace(link.Title)
                && link.Title.Contains(text, StringComparison.Ordinal));

            if (matchedLink is not null && !string.IsNullOrWhiteSpace(matchedLink.Url))
            {
                var resolvedUrl = NormalizeAbsoluteUrl(matchedLink.Url, snapshot.Url);
                if (!string.IsNullOrWhiteSpace(resolvedUrl))
                {
                    if (!string.IsNullOrWhiteSpace(CurrentUrl))
                    {
                        _history.Push(CurrentUrl);
                    }

                    CurrentUrl = resolvedUrl;
                }
            }

            return Task.FromResult(string.Empty);
        }

        public Task<string> SelectOptionAsync(
            string fieldText,
            string optionText,
            CancellationToken cancellationToken = default)
        {
            SelectHistory.Add($"{fieldText}:{optionText}");
            return Task.FromResult(string.Empty);
        }

        public Task<string> ClickActionInSectionAsync(
            string sectionText,
            string actionText,
            CancellationToken cancellationToken = default)
        {
            ClickHistory.Add($"{sectionText}:{actionText}");
            return ClickAsync(actionText, cancellationToken);
        }

        public Task<string> GetPageContentAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<IReadOnlyList<SearchResultLink>> GetLinksAsync(
            int maxResults = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SearchResultLink>>(GetCurrentSnapshot().Links);

        public Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> GoBackAsync(CancellationToken cancellationToken = default)
        {
            if (_history.Count > 0)
            {
                CurrentUrl = _history.Pop();
            }

            return Task.FromResult(string.Empty);
        }

        private PageSnapshot GetCurrentSnapshot()
        {
            if (!string.IsNullOrWhiteSpace(CurrentUrl)
                && _snapshotsByUrl.TryGetValue(CurrentUrl, out var snapshotByUrl))
            {
                return snapshotByUrl;
            }

            return Snapshot ?? new PageSnapshot(
                CurrentUrl ?? string.Empty, null, string.Empty, [], [], [], []);
        }

        private static string? NormalizeAbsoluteUrl(string? candidate, string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
            {
                if (string.Equals(absolute.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return absolute.AbsoluteUri;
                }
            }

            if (!string.IsNullOrWhiteSpace(baseUrl)
                && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
                && Uri.TryCreate(baseUri, candidate, out var resolved))
            {
                return resolved.AbsoluteUri;
            }

            return null;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeCommandBus : ICommandBus
    {
        public List<string> PublishedCommandNames { get; } = [];
        public HashSet<string> CreatedRaceIds { get; } = [];

        public Task<TExecutionResult> PublishAsync<TAggregate, TIdentity, TExecutionResult>(
            ICommand<TAggregate, TIdentity, TExecutionResult> command,
            CancellationToken cancellationToken)
            where TAggregate : IAggregateRoot<TIdentity>
            where TIdentity : IIdentity
            where TExecutionResult : IExecutionResult
        {
            PublishedCommandNames.Add(command.GetType().Name);
            if (command is CreateRaceCommand createRace)
            {
                CreatedRaceIds.Add(createRace.AggregateId.Value);
            }

            return Task.FromResult((TExecutionResult)(IExecutionResult)ExecutionResult.Success());
        }
    }

    /// <summary>
    /// FakeCommandBus と連携し、CreateRaceCommand 発行後の
    /// RacePredictionContextReadModel クエリに有効なモデルを返す。
    /// </summary>
    private sealed class CollaboratingFakeQueryProcessor : IQueryProcessor
    {
        private readonly FakeCommandBus _bus;

        public CollaboratingFakeQueryProcessor(FakeCommandBus bus) => _bus = bus;

        public Task<TResult> ProcessAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
        {
            if (query is ReadModelByIdQuery<RacePredictionContextReadModel> raceQuery &&
                _bus.CreatedRaceIds.Contains(raceQuery.Id))
            {
                var model = new RacePredictionContextReadModel();
                model.SetTestData(raceQuery.Id, DateOnly.MinValue, "test", 0, "test");
                return Task.FromResult((TResult)(object)model);
            }

            return Task.FromResult(default(TResult)!);
        }
    }

    private sealed class FakeRaceQueryService : IRaceQueryService
    {
        public List<RaceSearchSummary> RegisteredRaces { get; } = [];

        public Task<IReadOnlyList<RaceSearchSummary>> SearchRegisteredRacesAsync(DateOnly raceDate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RaceSearchSummary>>(RegisteredRaces.Where(x => x.RaceDate == raceDate).ToList());

        public Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(string raceId, CancellationToken cancellationToken = default)
            => Task.FromResult<RacePredictionContextReadModel?>(null);

        public Task<HorseReadModel?> GetHorseAsync(string horseId, CancellationToken cancellationToken = default)
            => Task.FromResult<HorseReadModel?>(null);

        public Task<JockeyReadModel?> GetJockeyAsync(string jockeyId, CancellationToken cancellationToken = default)
            => Task.FromResult<JockeyReadModel?>(null);

        public Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default)
            => Task.FromResult<MemoBySubjectReadModel?>(null);

        public Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(string horseId, CancellationToken cancellationToken = default)
            => Task.FromResult<HorseRaceHistoryReadModel?>(null);

        public Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(string jockeyId, CancellationToken cancellationToken = default)
            => Task.FromResult<JockeyRaceHistoryReadModel?>(null);
    }
}
