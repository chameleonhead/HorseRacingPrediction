namespace HorseRacingPrediction.Api.Contracts;

public sealed record PredictionMarkResponse(
    string EntryId,
    string MarkCode,
    int PredictedRank,
    decimal Score,
    string? Comment,
    string? HorseId = null,
    string? HorseName = null);
