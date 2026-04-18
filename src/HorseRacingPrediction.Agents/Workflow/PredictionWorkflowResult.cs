namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// <see cref="PredictionWorkflow.RunAsync"/> の実行結果。
/// 各ステップの出力を保持する。
/// </summary>
public sealed record PredictionWorkflowResult(
    string RaceId,
    string RaceContext,
    string HorseAnalysis,
    string PredictionSummary);
