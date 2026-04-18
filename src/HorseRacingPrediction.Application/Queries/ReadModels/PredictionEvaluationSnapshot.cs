namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record PredictionEvaluationSnapshot(
    DateTimeOffset EvaluatedAt,
    int EvaluationRevision,
    IReadOnlyList<string> HitTypeCodes,
    decimal? ScoreSummary,
    decimal? ReturnAmount,
    decimal? Roi);
