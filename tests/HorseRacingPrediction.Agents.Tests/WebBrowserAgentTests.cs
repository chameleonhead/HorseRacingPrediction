using HorseRacingPrediction.Agents.Agents;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public class WebBrowserAgentTests
{
    [TestMethod]
    public void SystemPrompt_MakesPlanningOptional()
    {
        StringAssert.Contains(WebBrowserAgent.SystemPrompt, "作業計画",
            "システムプロンプトに計画フェーズが含まれること");
        StringAssert.Contains(WebBrowserAgent.SystemPrompt, "作業計画は任意",
            "計画が任意であることが明示されていること");
        StringAssert.Contains(WebBrowserAgent.SystemPrompt, "その後はすぐに必要なツールを呼ぶ",
            "計画後にすぐ実行へ移ることが明示されていること");
    }

    [TestMethod]
    public void SystemPrompt_RequiresOpeningTargetPageInsteadOfUsingSearchSnippet()
    {
        StringAssert.Contains(WebBrowserAgent.SystemPrompt, "検索結果のタイトルやスニペットだけで結論を出さない",
            "検索結果のスニペットだけで回答しないルールが含まれること");
        StringAssert.Contains(WebBrowserAgent.SystemPrompt, "実際に対象ページを開いて確認する",
            "対象ページを開くことを明示していること");
    }

    [TestMethod]
    public void SystemPrompt_UsesChromeAddressBarAndStatelessPageReads()
    {
        StringAssert.Contains(WebBrowserAgent.SystemPrompt, "Chrome を前提",
            "Chrome 前提であることが明示されていること");
        StringAssert.Contains(WebBrowserAgent.SystemPrompt, "ページ取得は常に単一ページごとに完結",
            "ページ取得が stateless であることが明示されていること");
        StringAssert.Contains(WebBrowserAgent.SystemPrompt, "BrowserReadPage",
            "単一ページ取得ツールを使うことが明示されていること");
        StringAssert.Contains(WebBrowserAgent.SystemPrompt, "BrowserReadPage が必要と判断した場合を除き、詳細表示のためのクリックを自分で指示しない",
            "詳細クリックをツール内判断に委ねるルールが含まれること");
    }

    [TestMethod]
    public void SystemPrompt_RequiresSearchResultsBeforeOpeningNamedSiteWithoutUrl()
    {
        StringAssert.Contains(WebBrowserAgent.SystemPrompt, "URL が明示されていなければ、対象サイト名やドメイン名が依頼に含まれていても、まず BrowserSearch で検索結果一覧を取得する",
            "URL 未指定時に検索結果一覧から始めることが明示されていること");
        StringAssert.Contains(WebBrowserAgent.SystemPrompt, "いきなりそのサイトを開かず必ず検索結果一覧を先に取得する",
            "サイト名が書かれていても直アクセスしないルールが含まれること");
    }

}