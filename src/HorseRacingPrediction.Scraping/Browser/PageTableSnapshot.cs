namespace HorseRacingPrediction.Scraping.Browser;

/// <summary>
/// ページ内テーブルの構造スナップショット。
/// </summary>
public sealed record PageTableSnapshot(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);