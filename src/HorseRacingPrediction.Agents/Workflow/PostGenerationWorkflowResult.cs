namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// <see cref="PostGenerationWorkflow"/> の実行結果。
/// </summary>
public sealed record PostGenerationWorkflowResult(
    string PredictionTicketId,
    string RaceId,
    string HonmeiDraft,
    string AnaDraft,
    string DataRationaleDraft,
    string PostText);
