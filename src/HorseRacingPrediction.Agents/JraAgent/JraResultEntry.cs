namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>着順エントリー。</summary>
public sealed record JraResultEntry(
    int? FinishPosition,
    int HorseNumber,
    int? GateNumber,
    string? HorseName,
    string? JockeyName,
    string? FinishTime,
    decimal? AssignedWeight,
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff);