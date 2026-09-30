namespace HorseRacingPrediction.Contracts;

public sealed record PredictionMarkDto(
    string EntryId,
    string MarkCode,
    int PredictedRank,
    decimal Score,
    string? Comment,
    string? HorseId = null,
    string? HorseName = null);
