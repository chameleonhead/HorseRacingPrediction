using EventFlow;
using EventFlow.Commands;
using EventFlow.Queries;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// 競馬予測のエンド・ツー・エンドワークフロー。
/// <list type="number">
///   <item><see cref="RaceContextAgent"/> — レース情報の収集</item>
///   <item><see cref="HorseAnalysisAgent"/> — 出走馬の分析</item>
///   <item><see cref="PredictionAgent"/> — 予測票の作成・確定</item>
/// </list>
/// の 3 ステップで予測を行い、確定した予測票の情報を返す。
/// </summary>
public sealed class PredictionWorkflow
{
    private readonly RaceContextAgent _raceContextAgent;
    private readonly HorseAnalysisAgent _horseAnalysisAgent;
    private readonly PredictionAgent _predictionAgent;

    public PredictionWorkflow(
        RaceContextAgent raceContextAgent,
        HorseAnalysisAgent horseAnalysisAgent,
        PredictionAgent predictionAgent)
    {
        _raceContextAgent = raceContextAgent;
        _horseAnalysisAgent = horseAnalysisAgent;
        _predictionAgent = predictionAgent;
    }

    /// <summary>
    /// 指定したレースの予測を実行し、作成された予測票の情報を返す。
    /// </summary>
    /// <param name="raceId">予測対象のレース ID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>予測票 ID と予測概要（Markdown 形式）</returns>
    public async Task<PredictionWorkflowResult> RunAsync(
        string raceId,
        CancellationToken cancellationToken = default)
    {
        // Step 1: レースコンテキストを収集する
        var raceContext = await _raceContextAgent.CollectContextAsync(raceId, cancellationToken);

        // Step 2: 出走馬を分析する
        var horseAnalysis = await _horseAnalysisAgent.AnalyzeHorsesAsync(raceContext, cancellationToken);

        // Step 3: 予測票を作成・確定する
        var predictionResult = await _predictionAgent.CreatePredictionAsync(
            raceId, raceContext, horseAnalysis, cancellationToken);

        return new PredictionWorkflowResult(raceId, raceContext, horseAnalysis, predictionResult);
    }

    /// <summary>
    /// <see cref="PredictionWorkflow"/> を DI なしで構築するファクトリメソッド。
    /// 各エージェントのカーネルにプラグインを個別に登録する。
    /// </summary>
    /// <param name="baseKernel">共通設定済みの Semantic Kernel インスタンス</param>
    /// <param name="queryProcessor">EventFlow クエリプロセッサー</param>
    /// <param name="commandBus">EventFlow コマンドバス</param>
    /// <param name="browser">Web ブラウザ抽象</param>
    /// <param name="webFetchOptions">Web 取得オプション（許可ドメインなど）</param>
    public static PredictionWorkflow Create(
        Kernel baseKernel,
        IQueryProcessor queryProcessor,
        ICommandBus commandBus,
        IWebBrowser browser,
        IOptions<WebFetchOptions> webFetchOptions)
    {
        var raceQueryTools = new RaceQueryTools(queryProcessor);
        var predictionWriteTools = new PredictionWriteTools(commandBus);
        var webFetchTools = new WebFetchTools(browser, webFetchOptions);

        // RaceContextAgent: RaceQuery + WebFetch
        var raceContextKernel = baseKernel.Clone();
        raceContextKernel.Plugins.AddFromObject(raceQueryTools, "RaceQuery");
        raceContextKernel.Plugins.AddFromObject(webFetchTools, "WebFetch");

        // HorseAnalysisAgent: RaceQuery + WebFetch
        var horseAnalysisKernel = baseKernel.Clone();
        horseAnalysisKernel.Plugins.AddFromObject(raceQueryTools, "RaceQuery");
        horseAnalysisKernel.Plugins.AddFromObject(webFetchTools, "WebFetch");

        // PredictionAgent: RaceQuery + PredictionWrite + WebFetch
        var predictionKernel = baseKernel.Clone();
        predictionKernel.Plugins.AddFromObject(raceQueryTools, "RaceQuery");
        predictionKernel.Plugins.AddFromObject(predictionWriteTools, "PredictionWrite");
        predictionKernel.Plugins.AddFromObject(webFetchTools, "WebFetch");

        return new PredictionWorkflow(
            new RaceContextAgent(raceContextKernel),
            new HorseAnalysisAgent(horseAnalysisKernel),
            new PredictionAgent(predictionKernel));
    }
}
