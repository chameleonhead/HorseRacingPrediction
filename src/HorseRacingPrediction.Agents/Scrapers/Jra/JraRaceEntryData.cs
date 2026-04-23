namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 出馬表における1頭分の出走馬データ。
/// </summary>
public sealed record JraRaceEntryData(
    int HorseNumber,
    int? GateNumber,
    string HorseName,
    string? JockeyName,
    decimal? Weight,
    string? SexAge,
    decimal? BodyWeight,
    decimal? BodyWeightDiff,
    string? TrainerName,
    string? OwnerName);
