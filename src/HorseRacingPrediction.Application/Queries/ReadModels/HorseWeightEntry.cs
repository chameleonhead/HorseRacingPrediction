namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record HorseWeightEntry(
    string RaceId,
    string EntryId,
    DateTimeOffset RecordedAt,
    decimal? DeclaredWeight,
    decimal? DeclaredWeightDiff);
