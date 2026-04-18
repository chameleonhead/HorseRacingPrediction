namespace HorseRacingPrediction.Domain.Predictions;

public sealed record PredictionMarkDetails(
    string EntryId,
    string MarkCode,
    int PredictedRank,
    decimal Score,
    string? Comment);
