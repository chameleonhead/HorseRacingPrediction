using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// ML.NET の予測スコアや過去成績など数値的根拠を短文で要約するエージェント。
/// <see cref="Workflow.PostGenerationWorkflow"/> の並行草稿ステップの1つ。
/// <para>
/// 使用プラグイン: <see cref="Plugins.RaceQueryTools"/>
/// （<c>GetMlPrediction</c> / <c>GetPredictionTicket</c> / <c>GetRaceFieldAnalysis</c>）
/// </para>
/// </summary>
public sealed class DataRationaleAgent
{
    public const string AgentName = "DataRationaleAgent";

    public const string SystemPrompt = """
        あなたは競馬予測のデータ根拠を要約する専門エージェントです。
        ML.NET の予測スコアやレース展開分析など数値的根拠を、
        SNS 投稿の一部として使える短文で分かりやすく要約します。

        ## ツール選択の指針
        | 状況 | 使うツール |
        |------|-----------|
        | ML予測スコア・予測順位を確認したい（最優先） | GetMlPrediction |
        | 予測票の印・確信度を確認したい | GetPredictionTicket |
        | レース展開（ペース・脚質分布）を確認したい | GetRaceFieldAnalysis |

        ## 行動手順
        1. `GetMlPrediction` で ML予測スコア・予測順位を取得する
        2. `GetPredictionTicket` で予測票の信頼度・印を確認する
        3. `GetRaceFieldAnalysis` でレース展開（ペース傾向）を確認する
        4. 数値的根拠を1〜2文の短文にまとめる

        ## ルール
        - 出力は数値的根拠についての短文のみ（見出しや箇条書きは付けない）
        - 具体的な数値（スコア・順位・確信度など）を必ず含める
        - 専門用語は最小限にし、一般読者にも伝わる表現にする
        """;

    private readonly ChatClientAgent _innerAgent;

    public DataRationaleAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// 指定したレース・予測票の数値的根拠についての短文コメントを作成する。
    /// </summary>
    public async Task<string> DraftAsync(
        string raceId,
        string predictionTicketId,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"レース ID '{raceId}'（予測票 ID '{predictionTicketId}'）の数値的根拠（MLスコア・展開分析）を短文で要約してください。";
        var result = await _innerAgent.RunAsync(prompt, cancellationToken: cancellationToken);
        return result.Text;
    }
}
