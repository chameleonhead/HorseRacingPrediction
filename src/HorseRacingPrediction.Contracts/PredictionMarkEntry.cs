namespace HorseRacingPrediction.Contracts;

public sealed record PredictionMarkEntry(
    string EntryId,
    string MarkCode,
    int PredictedRank,
    decimal Score,
    string? Comment);
