using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// PlaywrightTools のモックブラウザを使ったユニットテスト。
/// セッションベースのブラウザ操作（ナビゲーション・クリック・リンク取得・検索・戻る）を検証する。
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
            SearchBaseUrl = "https://www.bing.com/search?q="
        });
        _sut = new PlaywrightTools(_fakeBrowser, options);
    }

    // ------------------------------------------------------------------ //
    // BrowserNavigate
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task BrowserNavigate_AllowedDomain_ReturnsPageText()
    {
        _fakeBrowser.ResponseText = "ページ本文テキスト";

        var result = await _sut.BrowserNavigate("https://www.jra.go.jp/race");

        StringAssert.Contains(result, "ページ本文テキスト");
        Assert.AreEqual("https://www.jra.go.jp/race", _fakeBrowser.LastNavigatedUrl);
    }

    [TestMethod]
    public async Task BrowserNavigate_BlockedDomain_ThrowsInvalidOperationException()
    {
        try
        {
            await _sut.BrowserNavigate("https://example.com/page");
            Assert.Fail("InvalidOperationException が発生すべきです");
        }
        catch (InvalidOperationException ex)
        {
            StringAssert.Contains(ex.Message, "example.com");
        }
    }

    [TestMethod]
    public async Task BrowserNavigate_InvalidUrl_ThrowsArgumentException()
    {
        try
        {
            await _sut.BrowserNavigate("not-a-valid-url");
            Assert.Fail("ArgumentException が発生すべきです");
        }
        catch (ArgumentException)
        {
            // expected
        }
    }

    [TestMethod]
    public async Task BrowserNavigate_EmptyAllowedDomains_ThrowsInvalidOperationException()
    {
        var options = Options.Create(new WebFetchOptions { AllowedDomains = [] });
        var sut = new PlaywrightTools(_fakeBrowser, options);

        try
        {
            await sut.BrowserNavigate("https://www.jra.go.jp/race");
            Assert.Fail("InvalidOperationException が発生すべきです");
        }
        catch (InvalidOperationException)
        {
            // expected
        }
    }

    [TestMethod]
    public async Task BrowserNavigate_SubdomainOfAllowedDomain_Allowed()
    {
        _fakeBrowser.ResponseText = "サブドメインページ";

        var result = await _sut.BrowserNavigate("https://sub.www.jra.go.jp/page");

        StringAssert.Contains(result, "サブドメインページ");
    }

    [TestMethod]
    public async Task BrowserNavigate_IncludesCurrentUrlInOutput()
    {
        _fakeBrowser.ResponseText = "本文";
        _fakeBrowser.SimulatedCurrentUrl = "https://www.jra.go.jp/race";

        var result = await _sut.BrowserNavigate("https://www.jra.go.jp/race");

        StringAssert.Contains(result, "[現在のページ: https://www.jra.go.jp/race]");
    }

    // ------------------------------------------------------------------ //
    // BrowserClick
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task BrowserClick_ReturnsPageTextAfterClick()
    {
        _fakeBrowser.SimulatedCurrentUrl = "https://www.jra.go.jp/race";
        _fakeBrowser.ClickResponseText = "クリック後のページ本文";

        var result = await _sut.BrowserClick("出馬表を見る");

        StringAssert.Contains(result, "クリック後のページ本文");
        Assert.AreEqual("出馬表を見る", _fakeBrowser.LastClickedText);
    }

    [TestMethod]
    public async Task BrowserClick_IncludesCurrentUrlInOutput()
    {
        _fakeBrowser.SimulatedCurrentUrl = "https://www.jra.go.jp/race/detail";
        _fakeBrowser.ClickResponseText = "詳細ページ";

        var result = await _sut.BrowserClick("詳細");

        StringAssert.Contains(result, "[現在のページ: https://www.jra.go.jp/race/detail]");
    }

    // ------------------------------------------------------------------ //
    // BrowserGetPageContent
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task BrowserGetPageContent_ReturnsCurrentPageText()
    {
        _fakeBrowser.ResponseText = "現在のページ内容";
        _fakeBrowser.SimulatedCurrentUrl = "https://www.jra.go.jp/page";

        var result = await _sut.BrowserGetPageContent();

        StringAssert.Contains(result, "現在のページ内容");
    }

    // ------------------------------------------------------------------ //
    // BrowserGetLinks
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task BrowserGetLinks_ReturnsFormattedMarkdownLinks()
    {
        _fakeBrowser.SimulatedCurrentUrl = "https://www.jra.go.jp/index.html";
        _fakeBrowser.CurrentPageLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/race", "レース情報"),
            new SearchResultLink("https://www.jra.go.jp/news", "ニュース"),
        ];

        var result = await _sut.BrowserGetLinks();

        StringAssert.Contains(result, "[レース情報](https://www.jra.go.jp/race)");
        StringAssert.Contains(result, "[ニュース](https://www.jra.go.jp/news)");
    }

    [TestMethod]
    public async Task BrowserGetLinks_NoLinks_ReturnsNotFoundMessage()
    {
        _fakeBrowser.CurrentPageLinks = [];

        var result = await _sut.BrowserGetLinks();

        StringAssert.Contains(result, "リンクが見つかりませんでした");
    }

    [TestMethod]
    public async Task BrowserGetLinks_RespectsMaxResults()
    {
        _fakeBrowser.CurrentPageLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/page1", "Page 1"),
            new SearchResultLink("https://www.jra.go.jp/page2", "Page 2"),
            new SearchResultLink("https://www.jra.go.jp/page3", "Page 3"),
        ];

        var result = await _sut.BrowserGetLinks(maxResults: 2);

        StringAssert.Contains(result, "Page 1");
        StringAssert.Contains(result, "Page 2");
        Assert.IsFalse(result.Contains("Page 3"), "maxResults を超えるリンクは含まれないこと");
    }

    // ------------------------------------------------------------------ //
    // BrowserSearch
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task BrowserSearch_ReturnsSearchResultText()
    {
        _fakeBrowser.SearchResponseText = "JRA レース情報 - www.jra.go.jp\nnetkeiba レース - db.netkeiba.com";

        var result = await _sut.BrowserSearch("皐月賞");

        StringAssert.Contains(result, "JRA レース情報");
        StringAssert.Contains(result, "netkeiba レース");
        StringAssert.Contains(_fakeBrowser.LastSearchQuery!, "皐月賞");
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
        _fakeBrowser.SearchResponseText = "";

        var result = await _sut.BrowserSearch("存在しない検索");

        StringAssert.Contains(result, "検索結果が見つかりませんでした");
    }

    // ------------------------------------------------------------------ //
    // BrowserGoBack
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task BrowserGoBack_ReturnsPreviousPageText()
    {
        _fakeBrowser.GoBackResponseText = "前のページの内容";
        _fakeBrowser.SimulatedCurrentUrl = "https://www.jra.go.jp/prev";

        var result = await _sut.BrowserGoBack();

        StringAssert.Contains(result, "前のページの内容");
        Assert.IsTrue(_fakeBrowser.GoBackCalled, "GoBackAsync が呼ばれること");
    }

    // ------------------------------------------------------------------ //
    // GetAITools registration
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void PlaywrightTools_GetAITools_HasExpectedFunctions()
    {
        var tools = _sut.GetAITools();

        Assert.AreEqual(6, tools.Count, "PlaywrightTools は 6 つ登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "BrowserNavigate"), "BrowserNavigate が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "BrowserClick"), "BrowserClick が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "BrowserGetPageContent"), "BrowserGetPageContent が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "BrowserGetLinks"), "BrowserGetLinks が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "BrowserSearch"), "BrowserSearch が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "BrowserGoBack"), "BrowserGoBack が登録されていること");
    }

    // ------------------------------------------------------------------ //
    // Fake browser implementation
    // ------------------------------------------------------------------ //

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
}
