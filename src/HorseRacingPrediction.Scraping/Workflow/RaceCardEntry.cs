namespace HorseRacingPrediction.Scraping.Workflow;

public sealed record RaceCardEntry(
    int HorseNumber,
    int? GateNumber,
    string HorseName,
    string? JockeyName,
    decimal? AssignedWeight,
    string? SexCode,
    int? Age,
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff,
    string? TrainerName);