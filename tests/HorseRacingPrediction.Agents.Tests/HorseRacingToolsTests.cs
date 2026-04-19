using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// HorseRacingTools のユニットテスト。
/// WebFetchTools の内部テストコンストラクタを使い、エージェント呼び出しをモックして検証する。
/// プロンプトに正しい URL やクエリが含まれることと、結果が適切にフォーマットされることを確認する。
/// </summary>
[TestClass]
public class HorseRacingToolsTests
{
    private HorseRacingTools _sut = null!;
    private string? _lastPrompt;
    private string _fakeResponse = "テスト応答";

    [TestInitialize]
    public void Setup()
    {
        _lastPrompt = null;
        var webFetchTools = new WebFetchTools(async (prompt, ct) =>
        {
            _lastPrompt = prompt;
            return _fakeResponse;
        });
        _sut = new HorseRacingTools(webFetchTools);
    }

    // ------------------------------------------------------------------ //
    // FetchRaceCard
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchRaceCard_ValidArgs_ReturnsMarkdownWithHeading()
    {
        _fakeResponse = "出走馬一覧テキスト";

        var result = await _sut.FetchRaceCard("05", "20241027", 11);

        StringAssert.Contains(result, "# 出馬表", "見出しが含まれること");
        StringAssert.Contains(result, "20241027", "日付が含まれること");
        StringAssert.Contains(result, "出走馬一覧テキスト", "本文が含まれること");
    }

    [TestMethod]
    public async Task FetchRaceCard_UsesJraUrl()
    {
        _fakeResponse = "dummy";

        await _sut.FetchRaceCard("05", "20241027", 11);

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "www.jra.go.jp",
            "JRA の URL がプロンプトに含まれること");
    }

    // ------------------------------------------------------------------ //
    // FetchJraEntryList
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchJraEntryList_SendsRaceNameAndJraSiteInPrompt()
    {
        _fakeResponse = "JRA公式の出走馬一覧";

        var result = await _sut.FetchJraEntryList("皐月賞");

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "皐月賞",
            "プロンプトにレース名が含まれること");
        StringAssert.Contains(_lastPrompt, "www.jra.go.jp",
            "プロンプトに JRA サイトドメインが含まれること");
        StringAssert.Contains(result, "JRA公式の出走馬一覧",
            "出走馬ページの本文が含まれること");
    }

    // ------------------------------------------------------------------ //
    // FetchHorseHistory
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchHorseHistory_ReturnsMarkdownWithHeading()
    {
        _fakeResponse = "戦績データ";

        var result = await _sut.FetchHorseHistory("イクイノックス");

        StringAssert.Contains(result, "## 馬名「イクイノックス」", "見出しが含まれること");
        StringAssert.Contains(result, "戦績データ", "本文が含まれること");
    }

    [TestMethod]
    public async Task FetchHorseHistory_UsesNetkeibaUrl()
    {
        _fakeResponse = "dummy";

        await _sut.FetchHorseHistory("イクイノックス");

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "db.netkeiba.com",
            "netkeiba の URL がプロンプトに含まれること");
    }

    // ------------------------------------------------------------------ //
    // FetchJockeyStats
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchJockeyStats_ReturnsMarkdownWithHeading()
    {
        _fakeResponse = "騎手成績データ";

        var result = await _sut.FetchJockeyStats("川田将雅");

        StringAssert.Contains(result, "## 騎手「川田将雅」", "見出しが含まれること");
        StringAssert.Contains(result, "騎手成績データ", "本文が含まれること");
    }

    [TestMethod]
    public async Task FetchJockeyStats_UsesNetkeibaUrl()
    {
        _fakeResponse = "dummy";

        await _sut.FetchJockeyStats("川田将雅");

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "db.netkeiba.com",
            "netkeiba の URL がプロンプトに含まれること");
    }

    // ------------------------------------------------------------------ //
    // FetchTrainerStats
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchTrainerStats_ReturnsMarkdownWithHeading()
    {
        _fakeResponse = "調教師成績データ";

        var result = await _sut.FetchTrainerStats("友道康夫");

        StringAssert.Contains(result, "## 調教師「友道康夫」", "見出しが含まれること");
        StringAssert.Contains(result, "調教師成績データ", "本文が含まれること");
    }

    [TestMethod]
    public async Task FetchTrainerStats_UsesNetkeibaUrl()
    {
        _fakeResponse = "dummy";

        await _sut.FetchTrainerStats("友道康夫");

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "db.netkeiba.com",
            "netkeiba の URL がプロンプトに含まれること");
        StringAssert.Contains(_lastPrompt, "trainer",
            "trainer の URL がプロンプトに含まれること");
    }

    // ------------------------------------------------------------------ //
    // FetchRaceResults
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task FetchRaceResults_WithYear_ReturnsMarkdownWithHeading()
    {
        _fakeResponse = "レース結果データ";

        var result = await _sut.FetchRaceResults("天皇賞秋", "2024");

        StringAssert.Contains(result, "## レース「天皇賞秋」", "レース名が含まれること");
        StringAssert.Contains(result, "2024年", "年度が含まれること");
        StringAssert.Contains(result, "レース結果データ", "本文が含まれること");
    }

    [TestMethod]
    public async Task FetchRaceResults_WithoutYear_ReturnsMarkdownWithHeading()
    {
        _fakeResponse = "レース結果データ";

        var result = await _sut.FetchRaceResults("天皇賞秋");

        StringAssert.Contains(result, "## レース「天皇賞秋」", "レース名が含まれること");
        StringAssert.Contains(result, "レース結果データ", "本文が含まれること");
    }

    [TestMethod]
    public async Task FetchRaceResults_SendsSearchQueryInPrompt()
    {
        _fakeResponse = "dummy";

        await _sut.FetchRaceResults("天皇賞秋", "2024");

        Assert.IsNotNull(_lastPrompt);
        StringAssert.Contains(_lastPrompt, "天皇賞秋",
            "プロンプトにレース名が含まれること");
        StringAssert.Contains(_lastPrompt, "db.netkeiba.com",
            "プロンプトに netkeiba サイトが含まれること");
    }

    // ------------------------------------------------------------------ //
    // GetAITools registration
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void HorseRacingTools_GetAITools_HasExpectedFunctions()
    {
        var tools = _sut.GetAITools();

        Assert.AreEqual(6, tools.Count, "競馬ツールは6つ登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FetchRaceCard"), "FetchRaceCard が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FetchJraEntryList"), "FetchJraEntryList が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FetchHorseHistory"), "FetchHorseHistory が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FetchJockeyStats"), "FetchJockeyStats が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FetchTrainerStats"), "FetchTrainerStats が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "FetchRaceResults"), "FetchRaceResults が登録されていること");
    }
}
