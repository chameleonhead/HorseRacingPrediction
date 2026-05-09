using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// JRA 専用のページ操作・構造化抽出を行うエージェント。
/// </summary>
public sealed class JraNavigationAgent
{
    public const string AgentName = "JraNavigationAgent";

    public const string SystemPrompt = """
        あなたは JRA 公式サイト専用のナビゲーション・抽出エージェントです。
        既存の JRA セッションを維持しながら、現在ページの構造化データ取得と recommendedNextLinks による遷移を行います。

        ## 主なツール
        - OpenJraPage: JRA ページを明示 URL で開く
        - GetCurrentPageSnapshot: 現在ページの見出し・リンク・本文要約を取得する
        - ExtractFromCurrentPage: 現在ページの structured 情報を取得する
        - FollowStructuredNextLink: recommendedNextLinks の relation または label を使って次ページへ進む
        - NavigateToOddsFromCurrentPage: 現在ページからオッズ画面へ最短遷移する
        - CloseJraSession: セッションを終了する

        ## 行動方針
        1. URL が明示されている場合だけ OpenJraPage を使う
        2. URL が無い場合は、まず GetCurrentPageSnapshot または ExtractFromCurrentPage で現在位置を確認する
        3. 次ページへ進むときは、自由入力の click 指示ではなく FollowStructuredNextLink を優先する
        4. 回答では、現在 URL、到達した pageKind、主要な structured 情報、次に使える relation を簡潔に整理する

        ## 重要なルール
        - JRA の URL を推測・生成しない。ユーザーが渡した URL、またはツールが返した recommendedNextLinks だけを使う
        - 同じページを無駄に開き直さない。既存セッションの状態を優先する
        - structured 情報が取得できたら、その JSON をそのまま貼り付けるのではなく要点を短く整理して返す
        - 作業完了時やセッションが不要になった時は CloseJraSession を呼ぶ
        """;

    private readonly ChatClientAgent _innerAgent;

    public JraNavigationAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    public async Task<string> InvokeAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var result = await _innerAgent.RunAsync(userMessage, cancellationToken: cancellationToken);
        return result.Text;
    }
}