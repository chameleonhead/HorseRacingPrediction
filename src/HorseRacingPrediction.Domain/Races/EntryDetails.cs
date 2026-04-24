namespace HorseRacingPrediction.Domain.Races;

public sealed record EntryDetails(
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
