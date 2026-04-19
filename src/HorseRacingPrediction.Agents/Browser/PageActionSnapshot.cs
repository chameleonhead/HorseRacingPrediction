namespace HorseRacingPrediction.Agents.Browser;

/// <summary>
/// ページ上の操作可能要素のスナップショット。
/// </summary>
public sealed record PageActionSnapshot(string Text, string Kind);