using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// 確定済み予測票の本命（◎）馬について、推す理由を短文で言語化するエージェント。
/// <see cref="Workflow.PostGenerationWorkflow"/> の並行草稿ステップの1つ。
/// <para>
/// 使用プラグイン: <see cref="Plugins.RaceQueryTools"/>
/// （<c>GetPredictionTicket</c> / <c>GetRacePredictionContext</c> / <c>GetHorseRaceStats</c> / <c>GetMemosBySubject</c>）
/// </para>
/// </summary>
public sealed class HonmeiCommentaryAgent
{
    public const string AgentName = "HonmeiCommentaryAgent";

    public const string SystemPrompt = """
        あなたは競馬の本命解説を担当する専門エージェントです。
        確定済みの予測票から本命（◎）馬を特定し、なぜその馬を推すのかを
        SNS 投稿の一部として使える短文で言語化します。

        ## ツール選択の指針
        | 状況 | 使うツール |
        |------|-----------|
        | 予測印・スコア・コメントを確認したい（最優先） | GetPredictionTicket |
        | レースの条件・出走馬一覧を確認したい | GetRacePredictionContext |
        | 本命馬の過去成績を掘り下げたい | GetHorseRaceStats |
        | 本命馬にまつわる注目情報を探したい | GetMemosBySubject |

        ## 行動手順
        1. `GetPredictionTicket` で予測票を取得し、markCode が ◎ の馬（本命）を特定する
        2. `GetHorseRaceStats` で本命馬の過去成績・適性スコアを確認する
        3. 必要なら `GetMemosBySubject`（subjectType: Horse）で追加情報を探す
        4. 本命馬を推す理由を2〜3文の短文にまとめる

        ## ルール
        - 出力は本命馬についての短文のみ（見出しや箇条書きは付けない）
        - 数値を1つ以上根拠として含める
        - 誇張・断定しすぎず、読み手が納得できる説明にする
        """;

    private readonly ChatClientAgent _innerAgent;

    public HonmeiCommentaryAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// 指定した予測票の本命馬についての短文コメントを作成する。
    /// </summary>
    public async Task<string> DraftAsync(
        string predictionTicketId,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"予測票 ID '{predictionTicketId}' の本命（◎）馬について、推す理由を短文で作成してください。";
        var result = await _innerAgent.RunAsync(prompt, cancellationToken: cancellationToken);
        return result.Text;
    }
}
