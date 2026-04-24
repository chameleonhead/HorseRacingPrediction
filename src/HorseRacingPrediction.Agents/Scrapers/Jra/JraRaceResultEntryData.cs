namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 成績ページにおける1頭分のレース結果データ。
/// </summary>
public sealed record JraRaceResultEntryData(
    int? FinishPosition,
    int HorseNumber,
    int? GateNumber,
    string HorseName,
    string? JockeyName,
    decimal? Weight,
    string? SexAge,
    string? OfficialTime,
    string? MarginText,
    string? LastThreeFurlongTime,
    decimal? BodyWeight,
    decimal? BodyWeightDiff,
    string? TrainerName,
    string? AbnormalResultCode);
