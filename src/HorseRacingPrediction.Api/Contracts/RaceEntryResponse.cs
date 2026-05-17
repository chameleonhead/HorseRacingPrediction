namespace HorseRacingPrediction.Api.Contracts;

public sealed record RaceEntryResponse(
    string EntryId,
    string HorseId,
    int HorseNumber,
    string? JockeyId,
    string? TrainerId,
    int? GateNumber,
    decimal? AssignedWeight,
    string? SexCode,
    int? Age,
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff,
    string? RunningStyleCode);
