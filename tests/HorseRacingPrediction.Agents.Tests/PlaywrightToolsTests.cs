using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// PlaywrightTools のモックブラウザを使ったユニットテスト。
/// ドメインバリデーション、ページ取得、リンク抽出、検索を検証する。
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

        Assert.AreEqual("ページ本文テキスト", result);
        Assert.AreEqual("https://www.jra.go.jp/race", _fakeBrowser.LastFetchedUrl);
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

        Assert.AreEqual("サブドメインページ", result);
    }

    // ------------------------------------------------------------------ //
    // BrowserGetLinks
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task BrowserGetLinks_ReturnsFormattedMarkdownLinks()
    {
        _fakeBrowser.LinksByUrl["https://www.jra.go.jp/index.html"] =
        [
            new SearchResultLink("https://www.jra.go.jp/race", "レース情報"),
            new SearchResultLink("https://www.jra.go.jp/news", "ニュース"),
        ];

        var result = await _sut.BrowserGetLinks("https://www.jra.go.jp/index.html");

        StringAssert.Contains(result, "[レース情報](https://www.jra.go.jp/race)");
        StringAssert.Contains(result, "[ニュース](https://www.jra.go.jp/news)");
    }

    [TestMethod]
    public async Task BrowserGetLinks_NoLinks_ReturnsNotFoundMessage()
    {
        var result = await _sut.BrowserGetLinks("https://www.jra.go.jp/empty");

        StringAssert.Contains(result, "リンクが見つかりませんでした");
    }

    [TestMethod]
    public async Task BrowserGetLinks_RespectsMaxResults()
    {
        _fakeBrowser.SearchResultLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/page1", "Page 1"),
            new SearchResultLink("https://www.jra.go.jp/page2", "Page 2"),
            new SearchResultLink("https://www.jra.go.jp/page3", "Page 3"),
        ];

        var result = await _sut.BrowserGetLinks("https://www.jra.go.jp/index.html", maxResults: 2);

        StringAssert.Contains(result, "Page 1");
        StringAssert.Contains(result, "Page 2");
        Assert.IsFalse(result.Contains("Page 3"), "maxResults を超えるリンクは含まれないこと");
    }

    [TestMethod]
    public async Task BrowserGetLinks_BlockedDomain_ThrowsInvalidOperationException()
    {
        try
        {
            await _sut.BrowserGetLinks("https://example.com/page");
            Assert.Fail("InvalidOperationException が発生すべきです");
        }
        catch (InvalidOperationException)
        {
            // expected
        }
    }

    // ------------------------------------------------------------------ //
    // BrowserSearch
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task BrowserSearch_ReturnsSearchResultLinks()
    {
        _fakeBrowser.SearchResultLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/race", "JRA レース情報"),
            new SearchResultLink("https://db.netkeiba.com/race", "netkeiba レース"),
        ];

        var result = await _sut.BrowserSearch("皐月賞");

        StringAssert.Contains(result, "[JRA レース情報]");
        StringAssert.Contains(result, "[netkeiba レース]");
        Assert.IsNotNull(_fakeBrowser.LastSearchQuery);
        StringAssert.Contains(_fakeBrowser.LastSearchQuery, "皐月賞");
    }

    [TestMethod]
    public async Task BrowserSearch_WithSite_AppendsSiteFilter()
    {
        _fakeBrowser.SearchResultLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/race", "JRA レース"),
        ];

        await _sut.BrowserSearch("皐月賞", site: "www.jra.go.jp");

        Assert.IsNotNull(_fakeBrowser.LastSearchQuery);
        StringAssert.Contains(_fakeBrowser.LastSearchQuery, "site:www.jra.go.jp",
            "site: フィルタがクエリに付加されること");
    }

    [TestMethod]
    public async Task BrowserSearch_NoLinks_FallsBackToPageText()
    {
        _fakeBrowser.SearchResultLinks = [];
        _fakeBrowser.ResponseText = "検索ページのテキスト";

        var result = await _sut.BrowserSearch("存在しない検索");

        StringAssert.Contains(result, "検索結果リンクを取得できませんでした");
    }

    [TestMethod]
    public async Task BrowserSearch_RespectsMaxResults()
    {
        _fakeBrowser.SearchResultLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/page1", "Page 1"),
            new SearchResultLink("https://www.jra.go.jp/page2", "Page 2"),
            new SearchResultLink("https://www.jra.go.jp/page3", "Page 3"),
        ];

        var result = await _sut.BrowserSearch("テスト", maxResults: 2);

        StringAssert.Contains(result, "Page 1");
        StringAssert.Contains(result, "Page 2");
        Assert.IsFalse(result.Contains("Page 3"), "maxResults を超えるリンクは含まれないこと");
    }

    // ------------------------------------------------------------------ //
    // BrowserSearchAndRead
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task BrowserSearchAndRead_FetchesTopPages()
    {
        _fakeBrowser.SearchResultLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/race1", "レース1"),
            new SearchResultLink("https://www.jra.go.jp/race2", "レース2"),
        ];
        _fakeBrowser.ResponseTexts["https://www.jra.go.jp/race1"] = "1着 カヴァレリッツォ";
        _fakeBrowser.ResponseTexts["https://www.jra.go.jp/race2"] = "出馬表データ";

        var result = await _sut.BrowserSearchAndRead("皐月賞", site: "www.jra.go.jp");

        StringAssert.Contains(result, "カヴァレリッツォ", "ページ本文が含まれること");
        StringAssert.Contains(result, "出馬表データ", "2ページ目の本文も含まれること");
        StringAssert.Contains(result, "レース1", "タイトルが含まれること");
        Assert.AreEqual(2, _fakeBrowser.FetchedUrls.Count, "2ページ分フェッチされること");
    }

    [TestMethod]
    public async Task BrowserSearchAndRead_RespectsMaxPages()
    {
        _fakeBrowser.SearchResultLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/page1", "Page 1"),
            new SearchResultLink("https://www.jra.go.jp/page2", "Page 2"),
            new SearchResultLink("https://www.jra.go.jp/page3", "Page 3"),
        ];
        _fakeBrowser.ResponseText = "ページ本文";

        var result = await _sut.BrowserSearchAndRead("テスト", maxPages: 2);

        Assert.AreEqual(2, _fakeBrowser.FetchedUrls.Count, "maxPages を超えてフェッチしないこと");
        Assert.IsFalse(result.Contains("Page 3"), "3ページ目は含まれないこと");
    }

    [TestMethod]
    public async Task BrowserSearchAndRead_NoLinks_FallsBackToSearchPage()
    {
        _fakeBrowser.SearchResultLinks = [];
        _fakeBrowser.ResponseText = "検索ページのテキスト";

        var result = await _sut.BrowserSearchAndRead("存在しない検索");

        StringAssert.Contains(result, "検索結果が見つかりませんでした");
    }

    [TestMethod]
    public async Task BrowserSearchAndRead_WithSite_AppendsSiteFilter()
    {
        _fakeBrowser.SearchResultLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/race", "JRA レース"),
        ];
        _fakeBrowser.ResponseText = "ページ本文";

        await _sut.BrowserSearchAndRead("皐月賞", site: "www.jra.go.jp");

        Assert.IsNotNull(_fakeBrowser.LastSearchQuery);
        StringAssert.Contains(_fakeBrowser.LastSearchQuery, "site:www.jra.go.jp",
            "site: フィルタがクエリに付加されること");
    }

    // ------------------------------------------------------------------ //
    // GetAITools registration
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void PlaywrightTools_GetAITools_HasExpectedFunctions()
    {
        var tools = _sut.GetAITools();

        Assert.AreEqual(4, tools.Count, "PlaywrightTools は 4 つ登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "BrowserNavigate"), "BrowserNavigate が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "BrowserGetLinks"), "BrowserGetLinks が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "BrowserSearch"), "BrowserSearch が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "BrowserSearchAndRead"), "BrowserSearchAndRead が登録されていること");
    }

    // ------------------------------------------------------------------ //
    // Fake browser implementation
    // ------------------------------------------------------------------ //

    private sealed class FakeWebBrowser : IWebBrowser
    {
        public string ResponseText { get; set; } = string.Empty;
        public Dictionary<string, string> ResponseTexts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IReadOnlyList<SearchResultLink>> LinksByUrl { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? LastFetchedUrl { get; private set; }
        public List<string> FetchedUrls { get; } = [];

        public IReadOnlyList<SearchResultLink> SearchResultLinks { get; set; } = [];
        public string? LastExtractLinksUrl { get; private set; }
        public string? LastSearchQuery { get; private set; }

        public Task<string> FetchTextAsync(string url, CancellationToken cancellationToken = default)
        {
            LastFetchedUrl = url;
            FetchedUrls.Add(url);

            if (ResponseTexts.TryGetValue(url, out var responseText))
            {
                return Task.FromResult(responseText);
            }

            return Task.FromResult(ResponseText);
        }

        public Task<IReadOnlyList<SearchResultLink>> ExtractLinksAsync(
            string url, int maxResults = 10, CancellationToken cancellationToken = default)
        {
            LastExtractLinksUrl = url;

            if (LinksByUrl.TryGetValue(url, out var pageLinks))
            {
                return Task.FromResult((IReadOnlyList<SearchResultLink>)pageLinks.Take(maxResults).ToList());
            }

            IReadOnlyList<SearchResultLink> result = SearchResultLinks.Take(maxResults).ToList();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<SearchResultLink>> SearchAsync(
            string query, int maxResults = 10, CancellationToken cancellationToken = default)
        {
            LastSearchQuery = query;

            IReadOnlyList<SearchResultLink> result = SearchResultLinks.Take(maxResults).ToList();
            return Task.FromResult(result);
        }
    }
}
