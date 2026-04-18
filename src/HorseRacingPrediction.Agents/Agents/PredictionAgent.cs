using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// レースコンテキストと馬分析結果をもとに、予測票（PredictionTicket）を作成・確定する
/// 自律型エージェント。
/// <para>
/// 使用プラグイン:
/// <list type="bullet">
///   <item><see cref="Plugins.RaceQueryTools"/> — レース・馬・騎手情報の照会</item>
///   <item><see cref="Plugins.PredictionWriteTools"/> — 予測票の作成・印追加・確定</item>
///   <item><see cref="Plugins.WebFetchTools"/> — 必要に応じた追加情報の検索</item>
/// </list>
/// </para>
/// </summary>
public sealed class PredictionAgent
{
    internal const string AgentName = "PredictionAgent";

    internal const string SystemPrompt = """
        あなたは競馬の予測票を作成する専門エージェントです。
        提供されたレースコンテキストと馬分析レポートをもとに、
        予測票を作成・確定してください。

        ## 作業手順
        1. `CreatePredictionTicket` で新しい予測票を作成する
           - predictorType: "AI"
           - predictorId: "PredictionAgent"
           - confidenceScore: 分析結果に基づいた信頼度（0.0〜1.0）
        2. `AddPredictionMark` で各出走馬に予測印を付ける
           - markCode: ◎（本命）、○（対抗）、▲（単穴）、△（連下）
           - predictedRank: 予測着順
           - score: 着順確信度
           - comment: 印をつけた根拠の簡単な説明
        3. 必要に応じて `AddPredictionRationale` で主要な判断根拠を追加する
        4. 最後に `FinalizePredictionTicket` で予測票を確定する

        ## 予測の方針
        - 過去成績・コース適性・騎手実績・馬場状態を総合的に判断する
        - 必ず本命（◎）1頭、対抗（○）1頭、単穴（▲）1頭を選ぶ
        - 連下（△）は2〜4頭まで選択可能
        - 不確実な場合は confidenceScore を低く設定する

        ## 出力
        確定した予測票の ID と予測結果の概要を Markdown 形式で返してください。
        """;

    private readonly ChatClientAgent _innerAgent;

    public PredictionAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// レースコンテキストと馬分析結果をもとに予測票を作成・確定し、
    /// 予測票 ID と予測概要を返す。
    /// </summary>
    /// <param name="raceId">対象レース ID</param>
    /// <param name="raceContext">RaceContextAgent が収集したレースコンテキスト</param>
    /// <param name="horseAnalysis">HorseAnalysisAgent が出力した馬分析レポート</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task<string> CreatePredictionAsync(
        string raceId,
        string raceContext,
        string horseAnalysis,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
            以下のレースコンテキストと馬分析結果をもとに、
            レース ID '{raceId}' の予測票を作成・確定してください。

            ## レースコンテキスト
            {raceContext}

            ## 馬分析レポート
            {horseAnalysis}
            """;

        var result = await _innerAgent.RunAsync(prompt, cancellationToken: cancellationToken);
        return result.Text;
    }
}
