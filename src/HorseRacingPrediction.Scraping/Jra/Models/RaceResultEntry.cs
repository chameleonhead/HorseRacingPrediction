namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>
/// レース結果の1頭分の情報。初期実装のため必要最低限のみ保持する。
/// </summary>
public sealed record RaceResultEntry(
    int FinishPosition,
    int HorseNumber,
    string HorseName,
    string? JockeyName,
    TimeSpan? Time);
