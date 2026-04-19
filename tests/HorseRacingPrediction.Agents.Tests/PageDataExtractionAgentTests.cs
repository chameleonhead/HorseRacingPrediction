using HorseRacingPrediction.Agents.Agents;
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
        var expectedOutput = "# レース情報\n\n東京競馬場で開催されるレースの情報です。";
        var chatClient = new FakeChatClient(expectedOutput);
        var agent = new PageDataExtractionAgent(chatClient);

        var rawText = "ログイン メニュー ヘッダー レース情報 東京競馬場で開催されるレースの情報です。 フッター お問い合わせ";
        var result = await agent.FormatPageContentAsync(rawText, "https://www.jra.go.jp/race");

        Assert.AreEqual(expectedOutput, result, "LLM の整形結果がそのまま返されること");
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
