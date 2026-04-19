using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// WeeklyScheduleWorkflow のユニットテスト。
/// フェイクの ChatClient・WebBrowser を使用し、ネットワークや LLM への依存を排除している。
/// </summary>
[TestClass]
public class WeeklyScheduleWorkflowTests
{
    private static readonly DateOnly SampleWeekend = new(2024, 10, 26); // 土曜日
    private static readonly DateOnly SampleThursday = new(2024, 10, 24); // 木曜日

    private FakeChatClient _fakeChatClient = null!;
    private WeeklyScheduleWorkflow _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeChatClient = new FakeChatClient();
        var browser = new FakeWebBrowser();
        var options = Options.Create(new WebFetchOptions
        {
            AllowedDomains = ["www.jra.go.jp", "db.netkeiba.com", "www.bing.com"],
            SearchBaseUrl = "https://www.bing.com/search?q="
        });
        var playwrightTools = new PlaywrightTools(browser, options);
        var webBrowserAgent = new WebBrowserAgent(_fakeChatClient, playwrightTools.GetAITools());
        _sut = WeeklyScheduleWorkflow.Create(_fakeChatClient, webBrowserAgent);
    }

    // ------------------------------------------------------------------ //
    // DiscoverRacesAsync
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task DiscoverRacesAsync_ValidJsonResponse_ReturnsRaceList()
    {
        _fakeChatClient.ResponseText = """
            ```json
            [
              {
                "raceName": "天皇賞秋",
                "raceDate": "2024-10-27",
                "racecourse": "東京",
                "raceNumber": 11,
                "raceQuery": "2024年天皇賞秋 東京11R",
                "horseNames": ["イクイノックス", "リバティアイランド"],
                "jockeyNames": ["川田将雅", "坂井瑠星"],
                "trainerNames": ["友道康夫", "作田誠二"]
              }
            ]
            ```
            """;

        var races = await _sut.DiscoverRacesAsync(SampleWeekend);

        Assert.AreEqual(1, races.Count, "1 レースが返されること");
        Assert.AreEqual("天皇賞秋", races[0].RaceName);
        Assert.AreEqual(new DateOnly(2024, 10, 27), races[0].RaceDate);
        Assert.AreEqual("東京", races[0].Racecourse);
        Assert.AreEqual(11, races[0].RaceNumber);
        Assert.AreEqual(2, races[0].HorseNames.Count, "馬名が 2 件");
        Assert.AreEqual(2, races[0].JockeyNames.Count, "騎手名が 2 件");
        Assert.AreEqual(2, races[0].TrainerNames.Count, "調教師名が 2 件");
    }

    [TestMethod]
    public async Task DiscoverRacesAsync_ThursdayDate_AdjustsToSaturday()
    {
        _fakeChatClient.ResponseText = "[]";

        // 木曜日を指定しても内部で土曜日に補正されてエラーにならないこと
        var races = await _sut.DiscoverRacesAsync(SampleThursday);

        Assert.IsNotNull(races);
    }

    [TestMethod]
    public async Task DiscoverRacesAsync_EmptyJsonArray_ReturnsEmptyList()
    {
        _fakeChatClient.ResponseText = "[]";

        var races = await _sut.DiscoverRacesAsync(SampleWeekend);

        Assert.AreEqual(0, races.Count, "空リストが返されること");
    }

    [TestMethod]
    public async Task DiscoverRacesAsync_InvalidJson_ReturnsEmptyList()
    {
        _fakeChatClient.ResponseText = "JSON ではないテキスト";

        var races = await _sut.DiscoverRacesAsync(SampleWeekend);

        Assert.AreEqual(0, races.Count, "解析失敗時は空リストを返すこと");
    }

    [TestMethod]
    public async Task DiscoverRacesAsync_MultipleRaces_ReturnsAll()
    {
        _fakeChatClient.ResponseText = """
            [
              {
                "raceName": "天皇賞秋",
                "raceDate": "2024-10-27",
                "racecourse": "東京",
                "raceNumber": 11,
                "raceQuery": "2024年天皇賞秋 東京11R",
                "horseNames": [],
                "jockeyNames": [],
                "trainerNames": []
              },
              {
                "raceName": "スワンS",
                "raceDate": "2024-10-26",
                "racecourse": "京都",
                "raceNumber": 11,
                "raceQuery": "2024年スワンS 京都11R",
                "horseNames": [],
                "jockeyNames": [],
                "trainerNames": []
              }
            ]
            """;

        var races = await _sut.DiscoverRacesAsync(SampleWeekend);

        Assert.AreEqual(2, races.Count, "複数レースが返されること");
        Assert.AreEqual("天皇賞秋", races[0].RaceName);
        Assert.AreEqual("スワンS", races[1].RaceName);
    }

    // ------------------------------------------------------------------ //
    // CollectDataAsync
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task CollectDataAsync_EmptyRaces_ReturnsEmptyCollections()
    {
        _fakeChatClient.ResponseText = "データ収集結果";

        var result = await _sut.CollectDataAsync(SampleWeekend, []);

        Assert.AreEqual(SampleWeekend, result.TargetWeekend, "TargetWeekend が設定されること");
        Assert.AreEqual(0, result.RaceCollections.Count, "レースが空の場合は空リスト");
    }

    [TestMethod]
    public async Task CollectDataAsync_SingleRace_ReturnsOneCollection()
    {
        _fakeChatClient.ResponseText = "レース情報";

        var races = new[]
        {
            new WeekendRaceInfo(
                "天皇賞秋",
                new DateOnly(2024, 10, 27),
                "東京",
                11,
                "2024年天皇賞秋 東京11R",
                ["イクイノックス"],
                ["川田将雅"],
                ["友道康夫"])
        };

        var result = await _sut.CollectDataAsync(SampleWeekend, races);

        Assert.AreEqual(SampleWeekend, result.TargetWeekend);
        Assert.AreEqual(1, result.RaceCollections.Count, "1 レース分の収集結果");
        Assert.AreEqual("2024年天皇賞秋 東京11R", result.RaceCollections[0].RaceQuery);
    }

    [TestMethod]
    public async Task CollectDataAsync_MultipleRaces_ReturnsAllCollections()
    {
        _fakeChatClient.ResponseText = "データ収集結果";

        var races = new[]
        {
            new WeekendRaceInfo("天皇賞秋", new DateOnly(2024, 10, 27), "東京", 11, "2024年天皇賞秋", [], [], []),
            new WeekendRaceInfo("スワンS", new DateOnly(2024, 10, 26), "京都", 11, "2024年スワンS", [], [], [])
        };

        var result = await _sut.CollectDataAsync(SampleWeekend, races);

        Assert.AreEqual(2, result.RaceCollections.Count, "2 レース分の収集結果");
    }

    // ------------------------------------------------------------------ //
    // CollectPostPositionsAndPredictAsync
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task CollectPostPositionsAndPredictAsync_EmptyRaces_ReturnsEmptyList()
    {
        _fakeChatClient.ResponseText = "予測結果";

        var results = await _sut.CollectPostPositionsAndPredictAsync([]);

        Assert.AreEqual(0, results.Count, "レースが空の場合は空リスト");
    }

    [TestMethod]
    public async Task CollectPostPositionsAndPredictAsync_SingleRace_ReturnsPrediction()
    {
        _fakeChatClient.ResponseText = "## 天皇賞秋 予測レポート\n◎ イクイノックス";

        var races = new[]
        {
            new WeekendRaceInfo(
                "天皇賞秋",
                new DateOnly(2024, 10, 27),
                "東京",
                11,
                "2024年天皇賞秋 東京11R",
                ["イクイノックス"],
                ["川田将雅"],
                ["友道康夫"])
        };

        var results = await _sut.CollectPostPositionsAndPredictAsync(races);

        Assert.AreEqual(1, results.Count, "1 件の予測結果");
        Assert.AreEqual("天皇賞秋", results[0].RaceInfo.RaceName, "レース名が設定されること");
        Assert.IsFalse(string.IsNullOrEmpty(results[0].PredictionSummary), "予測レポートが返されること");
        Assert.IsNotNull(results[0].CollectionResult, "収集結果が設定されること");
    }

    [TestMethod]
    public async Task CollectPostPositionsAndPredictAsync_MultipleRaces_ReturnsAllPredictions()
    {
        _fakeChatClient.ResponseText = "予測結果";

        var races = new[]
        {
            new WeekendRaceInfo("天皇賞秋", new DateOnly(2024, 10, 27), "東京", 11, "2024年天皇賞秋", [], [], []),
            new WeekendRaceInfo("スワンS", new DateOnly(2024, 10, 26), "京都", 11, "2024年スワンS", [], [], [])
        };

        var results = await _sut.CollectPostPositionsAndPredictAsync(races);

        Assert.AreEqual(2, results.Count, "2 件の予測結果");
    }

    // ------------------------------------------------------------------ //
    // Fake implementations
    // ------------------------------------------------------------------ //

    private sealed class FakeChatClient : IChatClient
    {
        public string ResponseText { get; set; } = "テスト応答";

        public ChatClientMetadata Metadata => new("fake-provider", null, "fake-model");

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
        {
            throw new NotImplementedException();
        }

        public object? GetService(Type serviceType, object? key = null) => null;

        public void Dispose() { }
    }

    private sealed class FakeWebBrowser : IWebBrowser
    {
        public string? CurrentUrl => "https://www.jra.go.jp";

        public Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default)
            => Task.FromResult($"ページ本文: {url}");

        public Task<string> ClickAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> GetPageContentAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<IReadOnlyList<SearchResultLink>> GetLinksAsync(
            int maxResults = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SearchResultLink>>([]);

        public Task<string> SearchAsync(
            string query, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> GoBackAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
