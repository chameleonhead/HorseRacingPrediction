namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// ページ本文の整形結果と、必要に応じた 1 回だけの詳細遷移判断を表す。
/// </summary>
public sealed record PageExtractionResult(
    string ContentMarkdown,
    bool ShouldFollowDetailLink,
    string? DetailLinkText);