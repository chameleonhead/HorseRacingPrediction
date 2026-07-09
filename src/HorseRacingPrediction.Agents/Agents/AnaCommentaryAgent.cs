using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// 確定済み予測票の単穴（▲）・連下（△）馬について、妙味や注目ポイントを
/// 短文で言語化するエージェント。<see cref="Workflow.PostGenerationWorkflow"/> の並行草稿ステップの1つ。
/// <para>
/// 使用プラグイン: <see cref="Plugins.RaceQueryTools"/>
/// （<c>GetPredictionTicket</c> / <c>GetRacePredictionContext</c> / <c>GetHorseRaceStats</c>）
/// </para>
/// </summary>
public sealed class AnaCommentaryAgent
{
    public const string AgentName = "AnaCommentaryAgent";

    public const string SystemPrompt = """
        あなたは競馬の穴馬解説を担当する専門エージェントです。
        確定済みの予測票から単穴（▲）・連下（△）馬を特定し、
        本命ほど注目されないが妙味のあるポイントを SNS 投稿の一部として使える短文で言語化します。

        ## ツール選択の指針
        | 状況 | 使うツール |
        |------|-----------|
        | 予測印・スコア・コメントを確認したい（最優先） | GetPredictionTicket |
        | レースの条件・出走馬一覧を確認したい | GetRacePredictionContext |
        | 穴馬の過去成績を掘り下げたい | GetHorseRaceStats |

        ## 行動手順
        1. `GetPredictionTicket` で予測票を取得し、markCode が ▲ または △ の馬を特定する
        2. `GetHorseRaceStats` で該当馬の過去成績・適性スコアを確認する
        3. 本命との違い・意外性を意識しつつ、狙い目となるポイントを2〜3文にまとめる

        ## ルール
        - 出力は穴馬についての短文のみ（見出しや箇条書きは付けない）
        - 該当馬が複数いる場合は最も妙味のある1頭に絞って言及する
        - 数値を1つ以上根拠として含める
        - 過度な期待を煽らず、「意外性」「一発」といった穴馬らしい語調にする
        """;

    private readonly ChatClientAgent _innerAgent;

    public AnaCommentaryAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// 指定した予測票の穴馬についての短文コメントを作成する。
    /// </summary>
    public async Task<string> DraftAsync(
        string predictionTicketId,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"予測票 ID '{predictionTicketId}' の穴馬（▲・△）について、妙味や注目ポイントを短文で作成してください。";
        var result = await _innerAgent.RunAsync(prompt, cancellationToken: cancellationToken);
        return result.Text;
    }
}
