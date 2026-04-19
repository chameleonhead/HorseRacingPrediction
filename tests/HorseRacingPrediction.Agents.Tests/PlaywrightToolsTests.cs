using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// PlaywrightTools のモックブラウザを使ったユニットテスト。
/// 単一ページ完結の検索・取得フローを検証する。
/// </summary>
[TestClass]
public class PlaywrightToolsTests
{
    private PlaywrightTools _sut = null!;
    private FakeWebBrowser _fakeBrowser = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeBrowser = new FakeWebBrowser();
        var options = Options.Create(new WebFetchOptions
        {
            AllowedDomains = ["www.jra.go.jp", "db.netkeiba.com", "www.bing.com"],
            SearchBaseUrl = "https://www.bing.com/search?q=",
            SearchResultsToFetch = 3,
            MaxLinksPerPage = 5,
        });
                var extractionAgent = new PageDataExtractionAgent(new StaticChatClient(
                        """
                        {
                            "contentMarkdown": "整形済み本文",
                            "shouldFollowDetailLink": false,
                            "detailLinkText": null
                        }
                        """));
        _sut = new PlaywrightTools(_fakeBrowser, options, extractionAgent);
    }

    [TestMethod]
    public async Task BrowserReadPage_AllowedDomain_ReturnsFormattedPageText()
    {
        _fakeBrowser.ResponseText = "ページ本文テキスト";
        _fakeBrowser.SimulatedCurrentUrl = "https://www.jra.go.jp/race";

        var result = await _sut.BrowserReadPage("https://www.jra.go.jp/race");

        StringAssert.Contains(result, "整形済み本文");
        Assert.AreEqual("https://www.jra.go.jp/race", _fakeBrowser.LastNavigatedUrl);
    }

    [TestMethod]
    public async Task BrowserReadPage_BlockedDomain_ThrowsInvalidOperationException()
    {
        try
        {
            await _sut.BrowserReadPage("https://example.com/page");
            Assert.Fail("InvalidOperationException が発生すべきです");
        }
        catch (InvalidOperationException ex)
        {
            StringAssert.Contains(ex.Message, "example.com");
        }
    }

    [TestMethod]
    public async Task BrowserReadPage_InvalidUrl_ThrowsArgumentException()
    {
        try
        {
            await _sut.BrowserReadPage("not-a-valid-url");
            Assert.Fail("ArgumentException が発生すべきです");
        }
        catch (ArgumentException)
        {
            // expected
        }
    }

    [TestMethod]
    public async Task BrowserReadPage_EmptyAllowedDomains_ThrowsInvalidOperationException()
    {
        var options = Options.Create(new WebFetchOptions { AllowedDomains = [] });
        var sut = new PlaywrightTools(_fakeBrowser, options);

        try
        {
            await sut.BrowserReadPage("https://www.jra.go.jp/race");
            Assert.Fail("InvalidOperationException が発生すべきです");
        }
        catch (InvalidOperationException)
        {
            // expected
        }
    }

    [TestMethod]
    public async Task BrowserReadPage_SubdomainOfAllowedDomain_Allowed()
    {
        _fakeBrowser.ResponseText = "サブドメインページ";

        var result = await _sut.BrowserReadPage("https://sub.www.jra.go.jp/page");

        StringAssert.Contains(result, "整形済み本文");
    }

    [TestMethod]
    public async Task BrowserReadPage_IncludesCurrentUrlAndLinksInOutput()
    {
        _fakeBrowser.ResponseText = "本文";
        _fakeBrowser.SimulatedCurrentUrl = "https://www.jra.go.jp/race";
        _fakeBrowser.CurrentPageLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/home", "ホーム", "header"),
            new SearchResultLink("https://www.jra.go.jp/detail", "詳細ページ"),
            new SearchResultLink("https://www.jra.go.jp/contact", "お問い合わせ", "footer")
        ];

        var result = await _sut.BrowserReadPage("https://www.jra.go.jp/race");

        StringAssert.Contains(result, "[現在のページ: https://www.jra.go.jp/race]");
        StringAssert.Contains(result, "## ヘッダーリンク");
        StringAssert.Contains(result, "[ホーム](https://www.jra.go.jp/home)");
        StringAssert.Contains(result, "## フッターリンク");
        StringAssert.Contains(result, "[お問い合わせ](https://www.jra.go.jp/contact)");
        StringAssert.Contains(result, "## リンク");
        StringAssert.Contains(result, "[詳細ページ](https://www.jra.go.jp/detail)");
    }

    [TestMethod]
    public async Task BrowserSearch_ReturnsSearchResultText()
    {
        _fakeBrowser.SearchResponseText = "JRA レース情報 - www.jra.go.jp\nnetkeiba レース - db.netkeiba.com";
        _fakeBrowser.SimulatedCurrentUrl = "https://www.bing.com/search?q=test";
        _fakeBrowser.CurrentPageLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/race", "JRA レース情報"),
            new SearchResultLink("https://db.netkeiba.com/race", "netkeiba レース"),
        ];

        var result = await _sut.BrowserSearch("皐月賞");

        StringAssert.Contains(result, "JRA レース情報");
        StringAssert.Contains(result, "netkeiba レース");
        StringAssert.Contains(_fakeBrowser.LastSearchQuery!, "皐月賞");
        StringAssert.Contains(result, "## 検索結果リンク");
    }

    [TestMethod]
    public async Task BrowserSearch_NormalizesRelativeLinksBeforeFormatting()
    {
        _fakeBrowser.SearchResponseText = "検索結果本文";
        _fakeBrowser.SimulatedCurrentUrl = "https://www.bing.com/search?q=test";
        _fakeBrowser.CurrentPageLinks =
        [
            new SearchResultLink("/race", "JRA レース情報"),
            new SearchResultLink("https://db.netkeiba.com/race", "netkeiba レース"),
        ];

        var result = await _sut.BrowserSearch("皐月賞");

        StringAssert.Contains(result, "[JRA レース情報](https://www.bing.com/race)");
        Assert.IsFalse(result.Contains("](/race)"), "相対 URL のまま出力されないこと");
    }

    [TestMethod]
    public async Task BrowserSearch_WithSite_AppendsSiteFilter()
    {
        _fakeBrowser.SearchResponseText = "JRA レース";

        await _sut.BrowserSearch("皐月賞", site: "www.jra.go.jp");

        StringAssert.Contains(_fakeBrowser.LastSearchQuery!, "site:www.jra.go.jp");
    }

    [TestMethod]
    public async Task BrowserSearch_NoResults_ReturnsNotFoundMessage()
    {
        _fakeBrowser.SearchResponseText = string.Empty;
        _fakeBrowser.CurrentPageLinks = [];

        var result = await _sut.BrowserSearch("存在しない検索");

        StringAssert.Contains(result, "検索結果が見つかりませんでした");
    }

    [TestMethod]
    public async Task BrowserSearch_UsesMaxSearchLinksPerPageForDisplayedLinks()
    {
        _fakeBrowser.SearchResponseText = "検索結果本文";
        _fakeBrowser.SimulatedCurrentUrl = "https://www.bing.com/search?q=test";
        _fakeBrowser.CurrentPageLinks = Enumerable.Range(1, 120)
            .Select(index => new SearchResultLink($"https://example.com/{index}", $"結果 {index}"))
            .ToList();

        var result = await _sut.BrowserSearch("皐月賞");

        StringAssert.Contains(result, "[結果 30](https://example.com/30)");
        Assert.IsFalse(result.Contains("[結果 31](https://example.com/31)"), "30件を超えるリンクは表示しないこと");
    }

    [TestMethod]
    public async Task BrowserReadPage_WhenExtractionRequestsDetail_ClicksOnce()
    {
        var agent = new PageDataExtractionAgent(new SequencedChatClient(
            """
            {
              "contentMarkdown": "概要ページ",
              "shouldFollowDetailLink": true,
              "detailLinkText": "詳細を表示"
            }
            """,
            """
            {
              "contentMarkdown": "詳細ページ本文",
              "shouldFollowDetailLink": false,
              "detailLinkText": null
            }
            """));
        var options = Options.Create(new WebFetchOptions
        {
            AllowedDomains = ["www.jra.go.jp", "www.bing.com"],
            SearchResultsToFetch = 3,
            MaxLinksPerPage = 5,
        });
        _sut = new PlaywrightTools(_fakeBrowser, options, agent);

        _fakeBrowser.ResponseText = "概要ページ 詳細を表示";
        _fakeBrowser.ClickResponseText = "詳細ページ本文";
        _fakeBrowser.SimulatedCurrentUrl = "https://www.jra.go.jp/race";
        _fakeBrowser.CurrentPageLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/race/detail", "詳細を表示")
        ];

        var result = await _sut.BrowserReadPage("https://www.jra.go.jp/race", "詳細を取得する");

        Assert.AreEqual("詳細を表示", _fakeBrowser.LastClickedText);
        StringAssert.Contains(result, "詳細ページ本文");
    }

    [TestMethod]
    public async Task BrowserReadPage_WhenDetailLinkTextIsAmbiguous_DoesNotClick()
    {
        var agent = new PageDataExtractionAgent(new StaticChatClient(
            """
            {
              "contentMarkdown": "概要ページ",
              "shouldFollowDetailLink": true,
              "detailLinkText": "詳細"
            }
            """));
        var options = Options.Create(new WebFetchOptions
        {
            AllowedDomains = ["www.jra.go.jp", "www.bing.com"],
            SearchResultsToFetch = 3,
            MaxLinksPerPage = 5,
        });
        _sut = new PlaywrightTools(_fakeBrowser, options, agent);

        _fakeBrowser.ResponseText = "概要ページ 詳細 詳細";
        _fakeBrowser.SimulatedCurrentUrl = "https://www.jra.go.jp/race";
        _fakeBrowser.CurrentPageLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/race/detail-1", "詳細"),
            new SearchResultLink("https://www.jra.go.jp/race/detail-2", "詳細")
        ];

        var result = await _sut.BrowserReadPage("https://www.jra.go.jp/race", "詳細を取得する");

        Assert.IsNull(_fakeBrowser.LastClickedText);
        StringAssert.Contains(result, "概要ページ");
    }

    [TestMethod]
    public async Task BrowserReadPage_NormalizesRelativeLinksBeforeFormatting()
    {
        _fakeBrowser.ResponseText = "本文";
        _fakeBrowser.SimulatedCurrentUrl = "https://www.jra.go.jp/race";
        _fakeBrowser.CurrentPageLinks =
        [
            new SearchResultLink("/home", "ホーム", "header"),
            new SearchResultLink("./detail", "詳細ページ"),
            new SearchResultLink("../contact", "お問い合わせ", "footer")
        ];

        var result = await _sut.BrowserReadPage("https://www.jra.go.jp/race");

        StringAssert.Contains(result, "[ホーム](https://www.jra.go.jp/home)");
        StringAssert.Contains(result, "[詳細ページ](https://www.jra.go.jp/detail)");
        StringAssert.Contains(result, "[お問い合わせ](https://www.jra.go.jp/contact)");
        Assert.IsFalse(result.Contains("](./detail)"), "相対 URL のまま出力されないこと");
    }

    [TestMethod]
    public void PlaywrightTools_GetAITools_HasExpectedFunctions()
    {
        var tools = _sut.GetAITools();

        Assert.AreEqual(2, tools.Count, "PlaywrightTools は 2 つ登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "BrowserSearch"), "BrowserSearch が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "BrowserReadPage"), "BrowserReadPage が登録されていること");
    }

    private sealed class FakeWebBrowser : IWebBrowser
    {
        public string ResponseText { get; set; } = string.Empty;
        public string ClickResponseText { get; set; } = string.Empty;
        public string GoBackResponseText { get; set; } = string.Empty;
        public string? SimulatedCurrentUrl { get; set; }

        public IReadOnlyList<SearchResultLink> CurrentPageLinks { get; set; } = [];
        public string SearchResponseText { get; set; } = string.Empty;

        public string? LastNavigatedUrl { get; private set; }
        public string? LastClickedText { get; private set; }
        public string? LastSearchQuery { get; private set; }
        public bool GoBackCalled { get; private set; }

        public string? CurrentUrl => SimulatedCurrentUrl;

        public Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default)
        {
            LastNavigatedUrl = url;
            SimulatedCurrentUrl = url;
            return Task.FromResult(ResponseText);
        }

        public Task<string> ClickAsync(string text, CancellationToken cancellationToken = default)
        {
            LastClickedText = text;
            return Task.FromResult(ClickResponseText);
        }

        public Task<string> GetPageContentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ResponseText);
        }

        public Task<IReadOnlyList<SearchResultLink>> GetLinksAsync(
            int maxResults = 10, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SearchResultLink> result = CurrentPageLinks.Take(maxResults).ToList();
            return Task.FromResult(result);
        }

        public Task<string> SearchAsync(
            string query, CancellationToken cancellationToken = default)
        {
            LastSearchQuery = query;
            return Task.FromResult(SearchResponseText);
        }

        public Task<string> GoBackAsync(CancellationToken cancellationToken = default)
        {
            GoBackCalled = true;
            return Task.FromResult(GoBackResponseText);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StaticChatClient : IChatClient
    {
        private readonly string _response;

        public StaticChatClient(string response) => _response = response;

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

    private sealed class SequencedChatClient : IChatClient
    {
        private readonly Queue<string> _responses;

        public SequencedChatClient(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public ChatOptions? DefaultOptions { get; set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var response = _responses.Count > 0 ? _responses.Dequeue() : string.Empty;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
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
