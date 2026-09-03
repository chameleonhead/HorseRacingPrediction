namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>
/// 出馬表の1頭分の情報。初期実装のため必要最低限のみ保持する。
/// </summary>
public sealed record RaceEntry(
    int HorseNumber,
    string HorseName,
    int? FrameNumber,
    string? JockeyName,
    decimal? AssignedWeight);
