namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record PredictionMarkSnapshot(
    string EntryId,
    string MarkCode,
    int PredictedRank,
    decimal Score,
    string? Comment);
