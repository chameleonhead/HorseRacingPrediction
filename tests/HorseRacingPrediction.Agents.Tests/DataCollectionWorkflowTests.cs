using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// DataCollectionWorkflow のユニットテスト。
/// 各データ収集エージェントはフェイクの ChatClient を使用し、
/// ネットワークや LLM への依存を排除している。
/// </summary>
[TestClass]
public class DataCollectionWorkflowTests
{
    private DataCollectionWorkflow _sut = null!;
    private FakeChatClient _fakeChatClient = null!;

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
        _sut = DataCollectionWorkflow.Create(_fakeChatClient, webBrowserAgent);
    }

    // ------------------------------------------------------------------ //
    // CollectAsync
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task CollectAsync_EmptyLists_ReturnsResultWithRaceData()
    {
        _fakeChatClient.ResponseText = "レース情報";

        var result = await _sut.CollectAsync(
            raceQuery: "2024年天皇賞秋",
            horseNames: [],
            jockeyNames: [],
            trainerNames: []);

        Assert.AreEqual("2024年天皇賞秋", result.RaceQuery, "RaceQuery が設定されること");
        Assert.IsFalse(string.IsNullOrEmpty(result.RaceData), "RaceData が返されること");
        Assert.IsEmpty(result.HorseDataByName, "馬データが空であること");
        Assert.IsEmpty(result.JockeyDataByName, "騎手データが空であること");
        Assert.IsEmpty(result.StableDataByName, "厩舎データが空であること");
    }

    [TestMethod]
    public async Task CollectAsync_WithHorses_ReturnsHorseDataForEach()
    {
        _fakeChatClient.ResponseText = "データ収集結果";

        var result = await _sut.CollectAsync(
            raceQuery: "2024年天皇賞秋",
            horseNames: ["イクイノックス", "リバティアイランド"],
            jockeyNames: [],
            trainerNames: []);

        Assert.IsTrue(result.HorseDataByName.ContainsKey("イクイノックス"), "イクイノックスのデータが含まれること");
        Assert.IsTrue(result.HorseDataByName.ContainsKey("リバティアイランド"), "リバティアイランドのデータが含まれること");
    }

    [TestMethod]
    public async Task CollectAsync_WithJockeys_ReturnsJockeyDataForEach()
    {
        _fakeChatClient.ResponseText = "データ収集結果";

        var result = await _sut.CollectAsync(
            raceQuery: "2024年天皇賞秋",
            horseNames: [],
            jockeyNames: ["川田将雅", "戸崎圭太"],
            trainerNames: []);

        Assert.IsTrue(result.JockeyDataByName.ContainsKey("川田将雅"), "川田将雅のデータが含まれること");
        Assert.IsTrue(result.JockeyDataByName.ContainsKey("戸崎圭太"), "戸崎圭太のデータが含まれること");
    }

    [TestMethod]
    public async Task CollectAsync_WithTrainers_ReturnsStableDataForEach()
    {
        _fakeChatClient.ResponseText = "データ収集結果";

        var result = await _sut.CollectAsync(
            raceQuery: "2024年天皇賞秋",
            horseNames: [],
            jockeyNames: [],
            trainerNames: ["友道康夫", "木村哲也"]);

        Assert.IsTrue(result.StableDataByName.ContainsKey("友道康夫"), "友道康夫のデータが含まれること");
        Assert.IsTrue(result.StableDataByName.ContainsKey("木村哲也"), "木村哲也のデータが含まれること");
    }

    [TestMethod]
    public async Task CollectAsync_AllCategories_ReturnsAllData()
    {
        _fakeChatClient.ResponseText = "データ収集結果";

        var result = await _sut.CollectAsync(
            raceQuery: "2024年天皇賞秋",
            horseNames: ["イクイノックス"],
            jockeyNames: ["川田将雅"],
            trainerNames: ["友道康夫"]);

        Assert.AreEqual("2024年天皇賞秋", result.RaceQuery);
        Assert.IsFalse(string.IsNullOrEmpty(result.RaceData));
        Assert.HasCount(1, result.HorseDataByName, "馬データが1件");
        Assert.HasCount(1, result.JockeyDataByName, "騎手データが1件");
        Assert.HasCount(1, result.StableDataByName, "厩舎データが1件");
    }

    [TestMethod]
    public async Task CollectAsync_RaceCardIsParsed_DeduplicatesJockeysAndTrainers()
    {
        _fakeChatClient.ResponseFactory = messages =>
        {
            var lastMessage = messages.Last().Text;
            return lastMessage.Contains("レース「2024年天皇賞秋」", StringComparison.Ordinal)
                ? """
## 出馬表
| 枠番 | 馬番 | 馬名 | 騎手 | 斤量 | 性齢 | 馬体重 | 調教師 |
|------|------|------|------|------|------|--------|--------|
| 1 | 1 | 馬A | 騎手A | 56.0 | 牡3 | 480(+2) | 厩舎A |
| 2 | 2 | 馬B | 騎手B | 55.0 | 牝4 | 470(-1) | 厩舎A |
| 3 | 3 | 馬C | 騎手A | 57.0 | 牡5 | 500(+0) | 厩舎B |
"""
                : "個別データ";
        };

        var result = await _sut.CollectAsync(
            raceQuery: "2024年天皇賞秋",
            horseNames: [],
            jockeyNames: [],
            trainerNames: []);

        Assert.HasCount(3, result.HorseDataByName, "馬は出馬表から3件抽出されること");
        Assert.HasCount(2, result.JockeyDataByName, "騎手は重複排除されること");
        Assert.HasCount(2, result.StableDataByName, "厩舎は重複排除されること");
        Assert.AreEqual(1, _fakeChatClient.Messages.Count(message => message.Contains("調教師・厩舎「厩舎A」", StringComparison.Ordinal)), "厩舎A は1回だけ調査されること");
    }

    // ------------------------------------------------------------------ //
    // Fake implementations
    // ------------------------------------------------------------------ //

    private sealed class FakeChatClient : IChatClient
    {
        public string ResponseText { get; set; } = "テスト応答";
        public Func<IReadOnlyList<ChatMessage>, string>? ResponseFactory { get; set; }
        public List<string> Messages { get; } = [];

        public ChatClientMetadata Metadata => new("fake-provider", null, "fake-model");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var materializedMessages = messages.ToList();
            var lastText = materializedMessages.LastOrDefault()?.Text ?? string.Empty;
            Messages.Add(lastText);
            var responseText = ResponseFactory?.Invoke(materializedMessages) ?? ResponseText;
            var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)]);
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
