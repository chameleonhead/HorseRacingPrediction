using EventFlow;
using EventFlow.Commands;
using EventFlow.Queries;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// 競馬予測のエンド・ツー・エンドワークフロー。
/// Microsoft Agent Framework の <see cref="WorkflowBuilder"/> を使用し、
/// <list type="number">
///   <item><see cref="RaceContextAgent"/> — レース情報の収集</item>
///   <item><see cref="HorseAnalysisAgent"/> — 出走馬の分析</item>
///   <item><see cref="PredictionAgent"/> — 予測票の作成・確定</item>
/// </list>
/// の 3 ステップを順次実行して予測を行い、確定した予測票の情報を返す。
/// </summary>
public sealed class PredictionWorkflow
{
    private readonly ChatClientAgent _raceContextAgent;
    private readonly ChatClientAgent _horseAnalysisAgent;
    private readonly ChatClientAgent _predictionAgent;

    public PredictionWorkflow(
        ChatClientAgent raceContextAgent,
        ChatClientAgent horseAnalysisAgent,
        ChatClientAgent predictionAgent)
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
        var workflow = new WorkflowBuilder(_raceContextAgent)
            .AddEdge(_raceContextAgent, _horseAnalysisAgent)
            .AddEdge(_horseAnalysisAgent, _predictionAgent)
            .Build();

        var outputs = new Dictionary<string, System.Text.StringBuilder>
        {
            [_raceContextAgent.Id] = new(),
            [_horseAnalysisAgent.Id] = new(),
            [_predictionAgent.Id] = new()
        };

        await using var run = await InProcessExecution.RunStreamingAsync(
            workflow,
            new ChatMessage(ChatRole.User, $"レース ID '{raceId}' の予測コンテキストを収集してください。"),
            cancellationToken: cancellationToken);

        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        await foreach (var evt in run.WatchStreamAsync(cancellationToken))
        {
            if (evt is AgentResponseUpdateEvent agentUpdate &&
                outputs.TryGetValue(agentUpdate.ExecutorId, out var sb))
            {
                sb.Append(agentUpdate.Update.Text);
            }
            else if (evt is WorkflowErrorEvent workflowError)
            {
                throw new InvalidOperationException(
                    "ワークフローエラーが発生しました。",
                    workflowError.Exception);
            }
            else if (evt is ExecutorFailedEvent executorFailed)
            {
                throw new InvalidOperationException(
                    $"エグゼキュータ '{executorFailed.ExecutorId}' が失敗しました。",
                    executorFailed.Data);
            }
        }

        return new PredictionWorkflowResult(
            raceId,
            outputs[_raceContextAgent.Id].ToString(),
            outputs[_horseAnalysisAgent.Id].ToString(),
            outputs[_predictionAgent.Id].ToString());
    }

    /// <summary>
    /// <see cref="PredictionWorkflow"/> を DI なしで構築するファクトリメソッド。
    /// 各エージェントに必要なツールを個別に設定する。
    /// </summary>
    /// <param name="chatClient">共通の <see cref="IChatClient"/> インスタンス</param>
    /// <param name="queryProcessor">EventFlow クエリプロセッサー</param>
    /// <param name="commandBus">EventFlow コマンドバス</param>
    /// <param name="browser">Web ブラウザ抽象</param>
    /// <param name="webFetchOptions">Web 取得オプション（許可ドメインなど）</param>
    public static PredictionWorkflow Create(
        IChatClient chatClient,
        IQueryProcessor queryProcessor,
        ICommandBus commandBus,
        IWebBrowser browser,
        IOptions<WebFetchOptions> webFetchOptions)
    {
        var raceQueryTools = new RaceQueryTools(queryProcessor);
        var predictionWriteTools = new PredictionWriteTools(commandBus);
        var playwrightTools = new PlaywrightTools(browser, webFetchOptions);
        var webBrowserAgent = new WebBrowserAgent(chatClient, playwrightTools.GetAITools());
        var webFetchTools = new WebFetchTools(webBrowserAgent);

        var raceQueryAiTools = raceQueryTools.GetAITools();
        var predictionWriteAiTools = predictionWriteTools.GetAITools();
        var webFetchAiTools = webFetchTools.GetAITools();

        var raceContextAgent = new ChatClientAgent(
            chatClient,
            name: RaceContextAgent.AgentName,
            instructions: RaceContextAgent.SystemPrompt,
            tools: [.. raceQueryAiTools, .. webFetchAiTools]);

        var horseAnalysisAgent = new ChatClientAgent(
            chatClient,
            name: HorseAnalysisAgent.AgentName,
            instructions: HorseAnalysisAgent.SystemPrompt,
            tools: [.. raceQueryAiTools, .. webFetchAiTools]);

        var predictionAgent = new ChatClientAgent(
            chatClient,
            name: PredictionAgent.AgentName,
            instructions: PredictionAgent.SystemPrompt,
            tools: [.. raceQueryAiTools, .. predictionWriteAiTools, .. webFetchAiTools]);

        return new PredictionWorkflow(raceContextAgent, horseAnalysisAgent, predictionAgent);
    }
}
