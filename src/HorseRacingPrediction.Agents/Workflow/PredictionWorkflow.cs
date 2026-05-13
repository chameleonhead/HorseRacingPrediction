using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

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
}
