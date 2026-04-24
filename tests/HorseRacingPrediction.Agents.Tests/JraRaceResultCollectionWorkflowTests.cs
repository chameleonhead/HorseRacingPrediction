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
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// JraRaceResultCollectionWorkflow のユニットテスト。
/// Fake 実装を使用してネットワーク・DB・LLM への依存を排除する。
/// </summary>
[TestClass]
public class JraRaceResultCollectionWorkflowTests
{
    private JraRaceResultCollectionWorkflow _sut = null!;
    private FakeChatClient _fakeChatClient = null!;
    private FakeWebBrowser _fakeWebBrowser = null!;
    private FakeCommandBus _fakeCommandBus = null!;
    private CollaboratingFakeQueryProcessor _fakeQueryProcessor = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeChatClient = new FakeChatClient();
        _fakeWebBrowser = new FakeWebBrowser();
        _fakeCommandBus = new FakeCommandBus();
        _fakeQueryProcessor = new CollaboratingFakeQueryProcessor(_fakeCommandBus);

        var discoveryAgent = new JraResultUrlDiscoveryAgent(_fakeChatClient, []);
        var scraper = new JraRaceResultScraper(_fakeWebBrowser);
        var writeTools = new DataCollectionWriteTools(_fakeCommandBus, _fakeQueryProcessor);

        _sut = new JraRaceResultCollectionWorkflow(discoveryAgent, scraper, writeTools);
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
    public async Task DiscoverUrlsAsync_ReturnsUrlsFromAgentResponse()
    {
        _fakeChatClient.ResponseText = """
            [
              {
                "url": "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01skd0203_20251026051101&sub=",
                "racecourse": "東京",
                "raceDate": "2025-10-26",
                "raceNumber": 11
              }
            ]
            """;

        var result = await _sut.DiscoverUrlsAsync(new DateOnly(2025, 10, 26));

        Assert.HasCount(1, result);
        Assert.AreEqual("東京", result[0].Racecourse);
        Assert.AreEqual("05", result[0].RacecourseCode, "CNAME から競馬場コードが解析されること");
        Assert.AreEqual(new DateOnly(2025, 10, 26), result[0].RaceDate);
        Assert.AreEqual(11, result[0].RaceNumber);
    }

    [TestMethod]
    public async Task DiscoverUrlsAsync_EmptyResponse_ReturnsEmptyList()
    {
        _fakeChatClient.ResponseText = "[]";

        var result = await _sut.DiscoverUrlsAsync(new DateOnly(2025, 10, 26));

        Assert.IsEmpty(result);
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
        _fakeChatClient.ResponseText = """
            [
              {
                "url": "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01skd0203_20251026051101&sub=",
                "racecourse": "東京",
                "raceDate": "2025-10-26",
                "raceNumber": 11
              }
            ]
            """;

        _fakeWebBrowser.Snapshot = new PageSnapshot(
            Url: "https://www.jra.go.jp/test",
            Title: "天皇賞（秋） 成績 | JRA",
            MainText: "2025年10月26日 東京 11R 天皇賞（秋） 芝・右 2000m GⅠ",
            Headings: ["天皇賞（秋）", "2025年10月26日 東京 11R"],
            Links: [],
            Actions: [],
            Tables:
            [
                new PageTableSnapshot(
                    ["着順", "馬番", "馬名", "騎手"],
                    [
                        ["1", "1", "イクイノックス", "川田将雅"],
                    ])
            ]);

        var result = await _sut.CollectAsync(new DateOnly(2025, 10, 26));

        Assert.AreEqual(new DateOnly(2025, 10, 26), result.RaceDate);
        Assert.HasCount(1, result.DiscoveredUrls, "URL が1件発見されること");
        Assert.HasCount(1, result.ScrapedResults, "成績が1件スクレイプされること");
        Assert.HasCount(1, result.SavedRaceIds, "レースが1件保存されること");
        Assert.IsEmpty(result.Errors);
    }

    // ------------------------------------------------------------------ //
    // Fake implementations
    // ------------------------------------------------------------------ //

    private sealed class FakeChatClient : IChatClient
    {
        public string ResponseText { get; set; } = "[]";

        public ChatClientMetadata Metadata => new("fake", null, "fake");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, ResponseText)]);
            return Task.FromResult(response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? key = null) => null;

        public void Dispose() { }
    }

    private sealed class FakeWebBrowser : IWebBrowser
    {
        public PageSnapshot? Snapshot { get; set; }

        public string? CurrentUrl => "https://www.jra.go.jp";

        public Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<PageSnapshot> GetPageSnapshotAsync(
            int maxLinks = 0,
            CancellationToken cancellationToken = default)
        {
            var snapshot = Snapshot ?? new PageSnapshot(
                CurrentUrl ?? string.Empty, null, string.Empty, [], [], [], []);
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
}
