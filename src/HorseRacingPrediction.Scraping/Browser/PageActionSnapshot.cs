namespace HorseRacingPrediction.Scraping.Browser;

/// <summary>
/// ページ上の操作可能要素のスナップショット。
/// </summary>
public sealed record PageActionSnapshot(string Text, string Kind);