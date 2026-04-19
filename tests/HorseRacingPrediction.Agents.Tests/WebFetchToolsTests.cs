using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// WebFetchTools のモックブラウザを使ったユニットテスト。
/// ネットワークに依存しない純粋な単体テスト。
/// </summary>
[TestClass]
public class WebFetchToolsTests
{
    private WebFetchTools _sut = null!;
    private FakeWebBrowser _fakeBrowser = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeBrowser = new FakeWebBrowser();
        var options = Options.Create(new WebFetchOptions
        {
            AllowedDomains = ["www.jra.go.jp", "db.netkeiba.com", "www.google.co.jp"],
            SearchBaseUrl = "https://www.google.co.jp/search?hl=ja&q="
        });
        _sut = new WebFetchTools(_fakeBrowser, options);
    }

    // ------------------------------------------------------------------ //
    // FetchPageContent
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchPageContent_AllowedDomain_ReturnsBrowserText()
    {
        _fakeBrowser.ResponseText = "レース情報ページ本文";

        var result = await _sut.FetchPageContent("https://www.jra.go.jp/race");

        Assert.AreEqual("レース情報ページ本文", result);
        Assert.AreEqual("https://www.jra.go.jp/race", _fakeBrowser.LastFetchedUrl);
    }

    [TestMethod]
    public async Task FetchPageContent_BlockedDomain_ThrowsInvalidOperationException()
    {
        try
        {
            await _sut.FetchPageContent("https://example.com/page");
            Assert.Fail("InvalidOperationException が発生すべきです");
        }
        catch (InvalidOperationException)
        {
            // expected
        }
    }

    [TestMethod]
    public async Task FetchPageContent_InvalidUrl_ThrowsArgumentException()
    {
        try
        {
            await _sut.FetchPageContent("not-a-valid-url");
            Assert.Fail("ArgumentException が発生すべきです");
        }
        catch (ArgumentException)
        {
            // expected
        }
    }

    // ------------------------------------------------------------------ //
    // SearchAndFetch
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task SearchAndFetch_NoSite_UsesQueryOnly()
    {
        _fakeBrowser.ResponseText = "検索結果テキスト";

        var result = await _sut.SearchAndFetch("天皇賞 出馬表");

        StringAssert.Contains(result, "検索結果テキスト",
            "フォールバック時は検索ページのテキストが含まれること");
        Assert.IsNotNull(_fakeBrowser.LastExtractLinksUrl,
            "ExtractLinksAsync が呼ばれること");
        StringAssert.Contains(_fakeBrowser.LastExtractLinksUrl, "google.co.jp",
            "Google の URL で検索されること");
        StringAssert.Contains(_fakeBrowser.LastExtractLinksUrl, "%e5%a4%a9%e7%9a%87%e8%b3%9e",
            "クエリが URL エンコードされること");
    }

    [TestMethod]
    public async Task SearchAndFetch_WithSite_AppendsSiteFilter()
    {
        _fakeBrowser.ResponseText = "絞り込み検索結果";

        await _sut.SearchAndFetch("天皇賞", "www.jra.go.jp");

        Assert.IsNotNull(_fakeBrowser.LastExtractLinksUrl);
        StringAssert.Contains(_fakeBrowser.LastExtractLinksUrl, "site%3awww.jra.go.jp",
            "site: フィルタが URL エンコードされて付加されること");
    }

    [TestMethod]
    public async Task SearchAndFetch_WithLinks_FetchesLinkedPages()
    {
        _fakeBrowser.SearchResultLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/race/page1", "JRA レース情報"),
            new SearchResultLink("https://db.netkeiba.com/race/123", "netkeiba レース"),
        ];
        _fakeBrowser.ResponseText = "ページ本文";

        var result = await _sut.SearchAndFetch("皐月賞 出走馬");

        Assert.AreEqual(2, _fakeBrowser.FetchedUrls.Count,
            "許可ドメインの2件がフェッチされること");
        StringAssert.Contains(result, "JRA レース情報",
            "リンクタイトルが含まれること");
        StringAssert.Contains(result, "ページ本文",
            "ページ本文が含まれること");
    }

    [TestMethod]
    public async Task SearchAndFetch_WithLinks_FiltersToAllowedDomains()
    {
        _fakeBrowser.SearchResultLinks =
        [
            new SearchResultLink("https://example.com/page", "外部サイト"),
            new SearchResultLink("https://www.jra.go.jp/race", "JRA レース"),
        ];
        _fakeBrowser.ResponseText = "JRAページ本文";

        await _sut.SearchAndFetch("皐月賞");

        Assert.AreEqual(1, _fakeBrowser.FetchedUrls.Count,
            "許可ドメインのリンクのみフェッチされること");
        Assert.AreEqual("https://www.jra.go.jp/race", _fakeBrowser.FetchedUrls[0]);
    }

    [TestMethod]
    public async Task SearchAndFetch_NoAllowedDomainLinks_FallsBackToSearchPage()
    {
        _fakeBrowser.SearchResultLinks =
        [
            new SearchResultLink("https://example.com/page", "外部サイト"),
        ];
        _fakeBrowser.ResponseText = "検索ページテキスト";

        var result = await _sut.SearchAndFetch("皐月賞");

        StringAssert.Contains(result, "検索ページテキスト",
            "許可ドメインがない場合は検索ページのテキストが返されること");
        Assert.AreEqual(1, _fakeBrowser.FetchedUrls.Count,
            "検索ページのフォールバック1回のみ");
        StringAssert.Contains(_fakeBrowser.FetchedUrls[0], "google.co.jp",
            "フォールバックは検索URLであること");
    }

    [TestMethod]
    public async Task SearchAndFetch_RespectsSearchResultsToFetchLimit()
    {
        // SearchResultsToFetch はデフォルト 3
        _fakeBrowser.SearchResultLinks =
        [
            new SearchResultLink("https://www.jra.go.jp/page1", "Page 1"),
            new SearchResultLink("https://www.jra.go.jp/page2", "Page 2"),
            new SearchResultLink("https://www.jra.go.jp/page3", "Page 3"),
            new SearchResultLink("https://www.jra.go.jp/page4", "Page 4"),
        ];
        _fakeBrowser.ResponseText = "ページ本文";

        await _sut.SearchAndFetch("テスト");

        Assert.AreEqual(3, _fakeBrowser.FetchedUrls.Count,
            "SearchResultsToFetch の件数までフェッチされること");
    }

    // ------------------------------------------------------------------ //
    // FetchRaceCard
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchRaceCard_ValidArgs_ReturnsMarkdownWithHeading()
    {
        _fakeBrowser.ResponseText = "出走馬一覧テキスト";

        var result = await _sut.FetchRaceCard("05", "20241027", 11);

        StringAssert.Contains(result, "# 出馬表", "見出しが含まれること");
        StringAssert.Contains(result, "20241027", "日付が含まれること");
        StringAssert.Contains(result, "出走馬一覧テキスト", "本文が含まれること");
    }

    [TestMethod]
    public async Task FetchRaceCard_UsesJraUrl()
    {
        _fakeBrowser.ResponseText = "dummy";

        await _sut.FetchRaceCard("05", "20241027", 11);

        StringAssert.Contains(_fakeBrowser.LastFetchedUrl, "www.jra.go.jp",
            "JRA の URL が使われること");
    }

    // ------------------------------------------------------------------ //
    // FetchHorseHistory
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchHorseHistory_ReturnsMarkdownWithHeading()
    {
        _fakeBrowser.ResponseText = "戦績データ";

        var result = await _sut.FetchHorseHistory("イクイノックス");

        StringAssert.Contains(result, "## 馬名「イクイノックス」", "見出しが含まれること");
        StringAssert.Contains(result, "戦績データ", "本文が含まれること");
    }

    [TestMethod]
    public async Task FetchHorseHistory_UsesNetkeibaUrl()
    {
        _fakeBrowser.ResponseText = "dummy";

        await _sut.FetchHorseHistory("イクイノックス");

        StringAssert.Contains(_fakeBrowser.LastFetchedUrl, "db.netkeiba.com",
            "netkeiba の URL が使われること");
    }

    // ------------------------------------------------------------------ //
    // FetchJockeyStats
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchJockeyStats_ReturnsMarkdownWithHeading()
    {
        _fakeBrowser.ResponseText = "騎手成績データ";

        var result = await _sut.FetchJockeyStats("川田将雅");

        StringAssert.Contains(result, "## 騎手「川田将雅」", "見出しが含まれること");
        StringAssert.Contains(result, "騎手成績データ", "本文が含まれること");
    }

    [TestMethod]
    public async Task FetchJockeyStats_UsesNetkeibaUrl()
    {
        _fakeBrowser.ResponseText = "dummy";

        await _sut.FetchJockeyStats("川田将雅");

        StringAssert.Contains(_fakeBrowser.LastFetchedUrl, "db.netkeiba.com",
            "netkeiba の URL が使われること");
    }

    // ------------------------------------------------------------------ //
    // FetchTrainerStats
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchTrainerStats_ReturnsMarkdownWithHeading()
    {
        _fakeBrowser.ResponseText = "調教師成績データ";

        var result = await _sut.FetchTrainerStats("友道康夫");

        StringAssert.Contains(result, "## 調教師「友道康夫」", "見出しが含まれること");
        StringAssert.Contains(result, "調教師成績データ", "本文が含まれること");
    }

    [TestMethod]
    public async Task FetchTrainerStats_UsesNetkeibaUrl()
    {
        _fakeBrowser.ResponseText = "dummy";

        await _sut.FetchTrainerStats("友道康夫");

        StringAssert.Contains(_fakeBrowser.LastFetchedUrl, "db.netkeiba.com",
            "netkeiba の URL が使われること");
        StringAssert.Contains(_fakeBrowser.LastFetchedUrl, "trainer",
            "trainer の URL が使われること");
    }

    // ------------------------------------------------------------------ //
    // FetchRaceResults
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchRaceResults_WithYear_ReturnsMarkdownWithHeading()
    {
        _fakeBrowser.ResponseText = "レース結果データ";

        var result = await _sut.FetchRaceResults("天皇賞秋", "2024");

        StringAssert.Contains(result, "## レース「天皇賞秋」", "レース名が含まれること");
        StringAssert.Contains(result, "2024年", "年度が含まれること");
        StringAssert.Contains(result, "レース結果データ", "本文が含まれること");
    }

    [TestMethod]
    public async Task FetchRaceResults_WithoutYear_ReturnsMarkdownWithHeading()
    {
        _fakeBrowser.ResponseText = "レース結果データ";

        var result = await _sut.FetchRaceResults("天皇賞秋");

        StringAssert.Contains(result, "## レース「天皇賞秋」", "レース名が含まれること");
        StringAssert.Contains(result, "レース結果データ", "本文が含まれること");
    }

    [TestMethod]
    public async Task FetchRaceResults_UsesSearchUrl()
    {
        _fakeBrowser.ResponseText = "dummy";

        await _sut.FetchRaceResults("天皇賞秋", "2024");

        Assert.IsNotNull(_fakeBrowser.LastExtractLinksUrl,
            "検索エンジン経由で検索されること");
        StringAssert.Contains(_fakeBrowser.LastExtractLinksUrl, "www.google.co.jp",
            "Google 検索 URL が使われること");
    }

    // ------------------------------------------------------------------ //
    // AllowedDomains = empty
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchPageContent_EmptyAllowedDomains_ThrowsInvalidOperationException()
    {
        var options = Options.Create(new WebFetchOptions { AllowedDomains = [] });
        var sut = new WebFetchTools(_fakeBrowser, options);

        try
        {
            await sut.FetchPageContent("https://www.jra.go.jp/race");
            Assert.Fail("InvalidOperationException が発生すべきです");
        }
        catch (InvalidOperationException)
        {
            // expected
        }
    }

    // ------------------------------------------------------------------ //
    // GetAITools registration
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void WebFetchTools_GetAITools_HasExpectedFunctions()
    {
        var tools = _sut.GetAITools();

        Assert.IsTrue(tools.Any(t => t.Name == "FetchPageContent"), "FetchPageContent が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "SearchAndFetch"), "SearchAndFetch が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FetchRaceCard"), "FetchRaceCard が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FetchHorseHistory"), "FetchHorseHistory が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FetchJockeyStats"), "FetchJockeyStats が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FetchTrainerStats"), "FetchTrainerStats が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FetchRaceResults"), "FetchRaceResults が登録されていること");
    }

    // ------------------------------------------------------------------ //
    // Fake browser implementation
    // ------------------------------------------------------------------ //

    private sealed class FakeWebBrowser : IWebBrowser
    {
        public string ResponseText { get; set; } = string.Empty;
        public string? LastFetchedUrl { get; private set; }
        public List<string> FetchedUrls { get; } = [];

        public IReadOnlyList<SearchResultLink> SearchResultLinks { get; set; } = [];
        public string? LastExtractLinksUrl { get; private set; }

        public Task<string> FetchTextAsync(string url, CancellationToken cancellationToken = default)
        {
            LastFetchedUrl = url;
            FetchedUrls.Add(url);
            return Task.FromResult(ResponseText);
        }

        public Task<IReadOnlyList<SearchResultLink>> ExtractLinksAsync(
            string url, int maxResults = 10, CancellationToken cancellationToken = default)
        {
            LastExtractLinksUrl = url;
            IReadOnlyList<SearchResultLink> result = SearchResultLinks.Take(maxResults).ToList();
            return Task.FromResult(result);
        }
    }
}
