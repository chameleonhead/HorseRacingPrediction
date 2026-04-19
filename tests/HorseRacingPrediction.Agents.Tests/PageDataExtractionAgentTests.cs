using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// PageDataExtractionAgent（ページ整形エージェント）のユニットテスト。
/// LLM を使わずに検証できるエッジケース（空入力・LLM 障害フォールバック）を中心にテストする。
/// </summary>
[TestClass]
public class PageDataExtractionAgentTests
{
    [TestMethod]
    public async Task FormatPageContentAsync_EmptyText_ReturnsEmpty()
    {
        var chatClient = new FakeChatClient("整形結果");
        var agent = new PageDataExtractionAgent(chatClient);

        var result = await agent.FormatPageContentAsync("", "https://example.com");

        Assert.AreEqual(string.Empty, result, "空テキストの場合は空文字を返すこと");
    }

    [TestMethod]
    public async Task FormatPageContentAsync_WhitespaceOnly_ReturnsEmpty()
    {
        var chatClient = new FakeChatClient("整形結果");
        var agent = new PageDataExtractionAgent(chatClient);

        var result = await agent.FormatPageContentAsync("   \n  ", "https://example.com");

        Assert.AreEqual(string.Empty, result, "空白のみの場合は空文字を返すこと");
    }

    [TestMethod]
    public async Task FormatPageContentAsync_ValidText_CallsLLMAndReturnsResult()
    {
        var expectedOutput = "# レース情報\n\n東京競馬場で開催されるレースの情報です。\n\n## ヘッダーリンク\n- [開催日程](https://example.com/schedule)\n\n## フッターリンク\n- [お問い合わせ](https://example.com/contact)";
        var chatClient = new FakeChatClient(expectedOutput);
        var agent = new PageDataExtractionAgent(chatClient);

        var rawText = "ログイン メニュー ヘッダー レース情報 東京競馬場で開催されるレースの情報です。 フッター お問い合わせ";
        var result = await agent.FormatPageContentAsync(
            rawText,
            "https://www.jra.go.jp/race",
            [
                new SearchResultLink("https://example.com/schedule", "開催日程"),
                new SearchResultLink("https://example.com/contact", "お問い合わせ")
            ]);

        Assert.AreEqual(expectedOutput, result, "LLM の整形結果がそのまま返されること");
    }

    [TestMethod]
    public async Task FormatPageContentAsync_RemovesUnverifiedUrlsFromLlmOutput()
    {
        var chatClient = new FakeChatClient("# 本文\n\n- [偽リンク](https://invalid.example.com/path)\n- [正規リンク](https://example.com/allowed)");
        var agent = new PageDataExtractionAgent(chatClient);

        var result = await agent.FormatPageContentAsync(
            "本文",
            "https://example.com/page",
            [new SearchResultLink("https://example.com/allowed", "正規リンク")]);

        Assert.IsFalse(result.Contains("https://invalid.example.com/path"), "未検証 URL は除去されること");
        StringAssert.Contains(result, "偽リンク");
        StringAssert.Contains(result, "[正規リンク](https://example.com/allowed)");
    }

    [TestMethod]
    public async Task FormatPageContentAsync_LLMReturnsEmpty_FallsBackToRawText()
    {
        var chatClient = new FakeChatClient("");
        var agent = new PageDataExtractionAgent(chatClient);

        var rawText = "ページの生テキスト";
        var result = await agent.FormatPageContentAsync(rawText, "https://example.com");

        Assert.AreEqual(rawText, result, "LLM が空を返した場合は生テキストにフォールバックすること");
    }

    [TestMethod]
    public async Task FormatPageContentAsync_LLMThrows_FallsBackToRawText()
    {
        var chatClient = new ThrowingChatClient();
        var agent = new PageDataExtractionAgent(chatClient);

        var rawText = "ページの生テキスト";
        var result = await agent.FormatPageContentAsync(rawText, "https://example.com");

        Assert.AreEqual(rawText, result, "LLM がエラーの場合は生テキストにフォールバックすること");
    }

    [TestMethod]
    public async Task FormatPageContentAsync_SearchResultsPage_ReturnsLinkCollectionFromPageLinks()
    {
        var chatClient = new ThrowingChatClient();
        var agent = new PageDataExtractionAgent(chatClient);

        var result = await agent.FormatPageContentAsync(
            "検索結果の生テキスト",
            "https://www.google.com/search?q=%E7%9A%90%E6%9C%88%E8%B3%9E",
            [
                new SearchResultLink("https://www.google.com/preferences", "設定", "header"),
                new SearchResultLink("https://www.jra.go.jp/keiba/satsuki/syutsuba.html", "皐月賞 - 出馬表 JRA日本中央競馬会"),
                new SearchResultLink("https://race.netkeiba.com/special/index.html", "皐月賞2026特集 | netkeiba")
            ]);

        StringAssert.Contains(result, "## 検索結果候補");
        StringAssert.Contains(result, "[皐月賞 - 出馬表 JRA日本中央競馬会](https://www.jra.go.jp/keiba/satsuki/syutsuba.html)");
        StringAssert.Contains(result, "[皐月賞2026特集 | netkeiba](https://race.netkeiba.com/special/index.html)");
        Assert.IsFalse(result.Contains("設定"), "検索結果ページでは周辺UIリンクを省くこと");
        Assert.IsFalse(result.Contains("検索結果の生テキスト"), "検索結果ページでは生テキストをそのまま返さないこと");
    }

    [TestMethod]
    public async Task FormatPageContentAsync_LongLinkList_TruncatesPromptLinks()
    {
        var chatClient = new CapturingChatClient("整形済み");
        var agent = new PageDataExtractionAgent(chatClient);
        var links = Enumerable.Range(1, 30)
            .Select(index => new SearchResultLink($"https://example.com/{index}", $"候補 {index}"))
            .ToList();

        await agent.FormatPageContentAsync("本文", "https://example.com", links);

        Assert.IsNotNull(chatClient.CapturedMessages, "LLM が呼ばれていること");
        var userMsg = chatClient.CapturedMessages!.Last(m => m.Role == ChatRole.User);
        StringAssert.Contains(userMsg.Text!, "... 他 10 件");
        Assert.IsFalse(userMsg.Text!.Contains("候補 21 | https://example.com/21"), "プロンプトに過剰なリンクを渡さないこと");
    }

    [TestMethod]
    public async Task FormatPageContentAsync_LongText_TruncatesInput()
    {
        var longText = new string('あ', 20_000);
        var chatClient = new CapturingChatClient("整形済み");
        var agent = new PageDataExtractionAgent(chatClient);

        await agent.FormatPageContentAsync(longText, "https://example.com");

        Assert.IsNotNull(chatClient.CapturedMessages, "LLM が呼ばれていること");
        var userMsg = chatClient.CapturedMessages!.Last(m => m.Role == ChatRole.User);
        Assert.IsTrue(
            userMsg.Text!.Length < longText.Length,
            "入力テキストが切り詰められていること");
    }

    [TestMethod]
    public async Task FormatPageContentAsync_Snapshot_IncludesStructuredSnapshotInPrompt()
    {
        var chatClient = new CapturingChatClient("整形済み");
        var agent = new PageDataExtractionAgent(chatClient);
        var snapshot = new PageSnapshot(
            "https://example.com/page",
            "ページタイトル",
            "本文",
            ["見出し1", "見出し2"],
            [new SearchResultLink("https://example.com/detail", "詳細")],
            [new PageActionSnapshot("もっと見る", "button")],
            [new PageTableSnapshot(["列1", "列2"], [["値1", "値2"]])]);

        await agent.FormatPageContentAsync(snapshot);

        Assert.IsNotNull(chatClient.CapturedMessages);
        var userMsg = chatClient.CapturedMessages!.Last(m => m.Role == ChatRole.User);
        StringAssert.Contains(userMsg.Text!, "--- ページ構造スナップショット(JSON) ---");
        StringAssert.Contains(userMsg.Text!, "\"Title\": ");
        StringAssert.Contains(userMsg.Text!, "\"Actions\": [");
        StringAssert.Contains(userMsg.Text!, "\"Kind\": \"button\"");
        StringAssert.Contains(userMsg.Text!, "\"Headers\": [");
    }

    [TestMethod]
    public async Task AnalyzePageAsync_ParsesStructuredResult()
    {
        var chatClient = new FakeChatClient("""
            {
              "contentMarkdown": "# 本文\n\n必要な本文だけ",
              "shouldFollowDetailLink": true,
              "detailLinkText": "詳細を表示"
            }
            """);
        var agent = new PageDataExtractionAgent(chatClient);

        var result = await agent.AnalyzePageAsync(
            "本文 詳細を表示 フッター",
            "https://example.com",
            "詳細を取得する",
            []);

        Assert.AreEqual("# 本文\n\n必要な本文だけ", result.ContentMarkdown);
        Assert.IsTrue(result.ShouldFollowDetailLink);
        Assert.AreEqual("詳細を表示", result.DetailLinkText);
    }

    [TestMethod]
    public async Task AnalyzePageAsync_InvalidResponse_FallsBackToRawTextWithoutFollowUp()
    {
        var chatClient = new FakeChatClient("JSON ではない応答");
        var agent = new PageDataExtractionAgent(chatClient);

        var result = await agent.AnalyzePageAsync(
            "ページの生テキスト",
            "https://example.com",
            null,
            []);

        Assert.AreEqual("ページの生テキスト", result.ContentMarkdown);
        Assert.IsFalse(result.ShouldFollowDetailLink);
        Assert.IsNull(result.DetailLinkText);
    }

    [TestMethod]
    public async Task AnalyzePageAsync_FencedJsonWithTrailingMetadata_ParsesJsonOnly()
    {
        var chatClient = new FakeChatClient(
            """
            ```json
            {
              "contentMarkdown": "ここから本文です",
              "shouldFollowDetailLink": true,
              "detailLinkText": "トップページへ戻る"
            }
            ```
            798 273 0 13.03300515462515 12.556 resp_xxx
            """);
        var agent = new PageDataExtractionAgent(chatClient);

        var result = await agent.AnalyzePageAsync(
            "ページの生テキスト",
            "https://www.jra.go.jp/",
            "詳細を取得する",
            [new SearchResultLink("https://www.jra.go.jp/", "トップページへ戻る")]);

        Assert.AreEqual("ここから本文です", result.ContentMarkdown);
        Assert.IsTrue(result.ShouldFollowDetailLink);
        Assert.AreEqual("トップページへ戻る", result.DetailLinkText);
    }

    // ------------------------------------------------------------------ //
    // Fake implementations
    // ------------------------------------------------------------------ //

    private sealed class FakeChatClient : IChatClient
    {
        private readonly string _response;

        public FakeChatClient(string response) => _response = response;

        public ChatOptions? DefaultOptions { get; set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose() { }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public ChatOptions? DefaultOptions { get; set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("LLM unavailable");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose() { }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private sealed class CapturingChatClient : IChatClient
    {
        private readonly string _response;

        public CapturingChatClient(string response) => _response = response;

        public IList<ChatMessage>? CapturedMessages { get; private set; }

        public ChatOptions? DefaultOptions { get; set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CapturedMessages = messages.ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose() { }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
