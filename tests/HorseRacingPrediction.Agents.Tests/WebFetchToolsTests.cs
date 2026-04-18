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
            AllowedDomains = ["www.jra.go.jp", "db.netkeiba.com", "www.bing.com"],
            SearchBaseUrl = "https://www.bing.com/search?q="
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
        _fakeBrowser.ResponseText = "検索結果";

        var result = await _sut.SearchAndFetch("天皇賞 出馬表");

        Assert.AreEqual("検索結果", result);
        StringAssert.Contains(_fakeBrowser.LastFetchedUrl, "bing.com",
            "Bing の URL が使われること");
        StringAssert.Contains(_fakeBrowser.LastFetchedUrl, "%e5%a4%a9%e7%9a%87%e8%b3%9e",
            "クエリが URL エンコードされること");
    }

    [TestMethod]
    public async Task SearchAndFetch_WithSite_AppendsSiteFilter()
    {
        _fakeBrowser.ResponseText = "絞り込み検索結果";

        await _sut.SearchAndFetch("天皇賞", "www.jra.go.jp");

        StringAssert.Contains(_fakeBrowser.LastFetchedUrl, "site%3awww.jra.go.jp",
            "site: フィルタが URL エンコードされて付加されること");
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
    }

    // ------------------------------------------------------------------ //
    // Fake browser implementation
    // ------------------------------------------------------------------ //

    private sealed class FakeWebBrowser : IWebBrowser
    {
        public string ResponseText { get; set; } = string.Empty;
        public string? LastFetchedUrl { get; private set; }

        public Task<string> FetchTextAsync(string url, CancellationToken cancellationToken = default)
        {
            LastFetchedUrl = url;
            return Task.FromResult(ResponseText);
        }
    }
}
