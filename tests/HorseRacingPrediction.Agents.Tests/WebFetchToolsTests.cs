using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// WebFetchTools のユニットテスト。
/// WebFetchTools は WebBrowserAgent に委譲するため、
/// 内部テストコンストラクタでエージェント呼び出しをモックして
/// プロンプト構成と応答パススルーを検証する。
/// ドメインバリデーションや低レベルブラウザ操作は <see cref="PlaywrightToolsTests"/> で検証。
/// </summary>
[TestClass]
public class WebFetchToolsTests
{
    private WebFetchTools _sut = null!;
    private string? _lastPrompt;
    private string _fakeResponse = "テスト応答";

    [TestInitialize]
    public void Setup()
    {
        _lastPrompt = null;
        _sut = new WebFetchTools(async (prompt, ct) =>
        {
            _lastPrompt = prompt;
            return _fakeResponse;
        });
    }

    // ------------------------------------------------------------------ //
    // FetchPageContent
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchPageContent_SendsUrlInPrompt()
    {
        _fakeResponse = "ページ本文テキスト";

        var result = await _sut.FetchPageContent("https://www.jra.go.jp/race");

        Assert.AreEqual("ページ本文テキスト", result);
        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "https://www.jra.go.jp/race",
            "プロンプトに URL が含まれること");
    }

    [TestMethod]
    public async Task FetchPageContent_PromptRequestsRawText()
    {
        await _sut.FetchPageContent("https://www.jra.go.jp/race");

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "単一ページの取得で完結",
            "単一ページ完結を要求するプロンプトであること");
        StringAssert.Contains(_lastPrompt, "不要部分は除去",
            "ノイズ除去を要求するプロンプトであること");
    }

    // ------------------------------------------------------------------ //
    // SearchWeb
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task SearchWeb_SendsQueryInPrompt()
    {
        _fakeResponse = "検索結果まとめ";

        var result = await _sut.SearchWeb("皐月賞 2026");

        Assert.AreEqual("検索結果まとめ", result);
        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "皐月賞 2026",
            "プロンプトに検索クエリが含まれること");
    }

    [TestMethod]
    public async Task SearchWeb_WithObjective_IncludesObjectiveInPrompt()
    {
        await _sut.SearchWeb("皐月賞", "出走馬を調べる");

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "出走馬を調べる",
            "プロンプトに目的が含まれること");
    }

    [TestMethod]
    public async Task SearchWeb_WithSite_IncludesSiteInPrompt()
    {
        await _sut.SearchWeb("皐月賞", site: "www.jra.go.jp");

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "www.jra.go.jp",
            "プロンプトにサイトドメインが含まれること");
    }

    [TestMethod]
    public async Task SearchWeb_WithSite_RequiresSearchResultListBeforeDirectSiteAccess()
    {
        await _sut.SearchWeb("皐月賞", site: "www.jra.go.jp");

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "そのサイトをいきなり開かず必ず最初に検索結果一覧を取得してください",
            "サイト指定があっても最初に検索結果一覧を取ることを要求すること");
    }

    [TestMethod]
    public async Task SearchWeb_WithoutOptional_OmitsOptionalFields()
    {
        await _sut.SearchWeb("テストクエリ");

        Assert.IsNotNull(_lastPrompt);
        Assert.IsFalse(_lastPrompt.Contains("調査目的:"),
            "目的省略時は目的フィールドが含まれないこと");
        Assert.IsFalse(_lastPrompt.Contains("対象サイト:"),
            "サイト省略時はサイトフィールドが含まれないこと");
    }

    // ------------------------------------------------------------------ //
    // ExploreFromEntryPoint
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task ExploreFromEntryPoint_SendsUrlAndObjectiveInPrompt()
    {
        _fakeResponse = "探索結果";

        var result = await _sut.ExploreFromEntryPoint(
            "https://www.jra.go.jp/keiba/g1/satsuki/index.html",
            "出走馬を調べる");

        Assert.AreEqual("探索結果", result);
        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "https://www.jra.go.jp/keiba/g1/satsuki/index.html",
            "プロンプトに起点 URL が含まれること");
        StringAssert.Contains(_lastPrompt, "出走馬を調べる",
            "プロンプトに目的が含まれること");
    }

    // ------------------------------------------------------------------ //
    // SearchAndFetch
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task SearchAndFetch_SendsQueryInPrompt()
    {
        _fakeResponse = "ページ本文";

        var result = await _sut.SearchAndFetch("天皇賞 出馬表");

        Assert.AreEqual("ページ本文", result);
        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "天皇賞 出馬表",
            "プロンプトに検索クエリが含まれること");
    }

    [TestMethod]
    public async Task SearchAndFetch_WithSite_IncludesSiteInPrompt()
    {
        await _sut.SearchAndFetch("天皇賞", "www.jra.go.jp");

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "www.jra.go.jp",
            "プロンプトにサイトドメインが含まれること");
    }

    // ------------------------------------------------------------------ //
    // SearchAndFetchContentAsync
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task SearchAndFetchContentAsync_SendsQueryInPrompt()
    {
        _fakeResponse = "ページ内容";

        var result = await _sut.SearchAndFetchContentAsync("皐月賞 出走馬情報", "www.jra.go.jp", maxLinksToFetch: 1);

        Assert.AreEqual("ページ内容", result);
        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "皐月賞 出走馬情報",
            "プロンプトに検索クエリが含まれること");
        StringAssert.Contains(_lastPrompt, "www.jra.go.jp",
            "プロンプトにサイトドメインが含まれること");
        StringAssert.Contains(_lastPrompt, "1",
            "プロンプトにページ数制限が含まれること");
    }

    [TestMethod]
    public async Task SearchAndFetchContentAsync_RequestsRawContent()
    {
        await _sut.SearchAndFetchContentAsync("テスト");

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "ページ取得は 1 ページずつ完結",
            "単一ページ完結を要求するプロンプトであること");
    }

    [TestMethod]
    public async Task SearchAndFetchContentAsync_RequiresSearchResultListBeforeDirectSiteAccess()
    {
        await _sut.SearchAndFetchContentAsync("テスト", "www.jra.go.jp");

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "そのサイトをいきなり開かず必ず最初に検索結果一覧を取得してください",
            "SearchAndFetch 系でも URL 未指定なら検索結果一覧から始めることを要求すること");
    }

    // ------------------------------------------------------------------ //
    // GetAITools registration
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void WebFetchTools_GetAITools_HasExpectedFunctions()
    {
        var tools = _sut.GetAITools();

        Assert.AreEqual(4, tools.Count, "汎用ツールは4つ登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FetchPageContent"), "FetchPageContent が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "SearchWeb"), "SearchWeb が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "ExploreFromEntryPoint"), "ExploreFromEntryPoint が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "SearchAndFetch"), "SearchAndFetch が登録されていること");
        Assert.IsFalse(tools.Any(t => t.Name == "FetchRaceCard"), "競馬ツールは含まれないこと");
        Assert.IsFalse(tools.Any(t => t.Name == "FetchJraEntryList"), "競馬ツールは含まれないこと");
    }
}
