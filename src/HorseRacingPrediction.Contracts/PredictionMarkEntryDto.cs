namespace HorseRacingPrediction.Contracts;

public sealed record PredictionMarkEntryDto(
    string EntryId,
    string MarkCode,
    int PredictedRank,
    decimal Score,
    string? Comment);
