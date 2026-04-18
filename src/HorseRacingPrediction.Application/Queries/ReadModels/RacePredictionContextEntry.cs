namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record RacePredictionContextEntry(
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
    decimal? DeclaredWeightDiff);
