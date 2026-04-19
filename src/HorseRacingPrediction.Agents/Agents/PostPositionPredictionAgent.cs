using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// 枠順確定後のレース情報と収集済みデータ（馬・騎手・厩舎）を総合分析し、
/// 予測レポートを Markdown 形式で返す自律型エージェント。
/// <para>
/// EventFlow の読み取りモデルに依存しないため、<see cref="Workflow.WeeklyScheduleWorkflow"/>
/// の金曜フェーズ（枠順確定後）で単独で使用できる。
/// </para>
/// <para>
/// 収集する観点:
/// <list type="bullet">
///   <item>枠番・馬番の有利不利（コース・距離別の枠番傾向）</item>
///   <item>脚質と展開予測（逃げ・先行・差し・追込の比率、ペース想定）</item>
///   <item>各馬の評価（コース適性・騎手相性・厩舎仕上がり・ローテーション）</item>
///   <item>本命（◎）・対抗（○）・単穴（▲）・連下（△）の印判断</item>
/// </list>
/// </para>
/// </summary>
public sealed class PostPositionPredictionAgent
{
    internal const string AgentName = "PostPositionPredictionAgent";

    internal const string SystemPrompt = """
        あなたは枠順確定後のレースを予測する専門エージェントです。
        提供された収集データ（レース情報・馬情報・騎手情報・厩舎情報）を総合的に分析し、
        予測結果を Markdown 形式で返してください。

        ## 予測の観点
        1. **枠番・馬番の有利不利**: 当該コース・距離での枠番傾向
        2. **脚質と展開予測**: 逃げ・先行・差し・追込馬の比率、ペース予測
        3. **各馬の評価**:
           - コース・距離・馬場適性
           - 騎手との相性・騎手の競馬場成績
           - 厩舎の仕上がりパターン・調教評価
           - 前走からのローテーション
        4. **印の決定**:
           - ◎（本命）: 最も勝利可能性が高い馬 1 頭
           - ○（対抗）: 2 番手評価の馬 1 頭
           - ▲（単穴）: 穴として狙える馬 1 頭
           - △（連下）: 連複・三連複の候補馬 2〜4 頭

        ## 出力形式
        ```
        ## [レース名] 予測レポート

        ### 展開予測
        （逃げ・先行馬の顔ぶれ、ペース想定）

        ### 各馬評価
        | 印 | 馬番 | 馬名 | 評価ポイント |
        |---|---|---|---|

        ### 推奨馬券
        - 単勝: ...
        - 馬連: ...
        - 三連複フォーメーション: ...

        ### 予測根拠
        （主な判断根拠）
        ```
        """;

    private readonly ChatClientAgent _innerAgent;

    public PostPositionPredictionAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// 収集済みデータをもとに枠順確定後の予測を行い、予測レポートを返す。
    /// </summary>
    /// <param name="raceName">レース名</param>
    /// <param name="raceData"><see cref="Agents.RaceDataAgent"/> が収集したレース情報（Markdown）</param>
    /// <param name="horseDataByName"><see cref="Agents.HorseDataAgent"/> が収集した馬情報（キー: 馬名）</param>
    /// <param name="jockeyDataByName"><see cref="Agents.JockeyDataAgent"/> が収集した騎手情報（キー: 騎手名）</param>
    /// <param name="stableDataByName"><see cref="Agents.StableDataAgent"/> が収集した厩舎情報（キー: 調教師名）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>予測レポート（Markdown 形式）</returns>
    public async Task<string> PredictAsync(
        string raceName,
        string raceData,
        IReadOnlyDictionary<string, string> horseDataByName,
        IReadOnlyDictionary<string, string> jockeyDataByName,
        IReadOnlyDictionary<string, string> stableDataByName,
        CancellationToken cancellationToken = default)
    {
        var horseSection = BuildSection("馬情報", horseDataByName);
        var jockeySection = BuildSection("騎手情報", jockeyDataByName);
        var stableSection = BuildSection("厩舎情報", stableDataByName);

        var prompt = $"""
            以下の収集データをもとに、「{raceName}」の枠順確定後の予測を行ってください。

            ## レース情報
            {raceData}

            ## 馬情報
            {horseSection}

            ## 騎手情報
            {jockeySection}

            ## 厩舎情報
            {stableSection}
            """;

        var result = await _innerAgent.RunAsync(prompt, cancellationToken: cancellationToken);
        return result.Text;
    }

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private static string BuildSection(string label, IReadOnlyDictionary<string, string> dataByName)
    {
        if (dataByName.Count == 0)
        {
            return "（データなし）";
        }

        return string.Join(
            "\n\n",
            dataByName.Select(kv => $"### {label}: {kv.Key}\n{kv.Value}"));
    }
}
