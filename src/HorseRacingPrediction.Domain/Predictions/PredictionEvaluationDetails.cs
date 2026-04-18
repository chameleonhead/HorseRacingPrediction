namespace HorseRacingPrediction.Domain.Predictions;

public sealed record PredictionEvaluationDetails(
    string RaceId,
    DateTimeOffset EvaluatedAt,
    int EvaluationRevision,
    IReadOnlyList<string> HitTypeCodes,
    decimal? ScoreSummary,
    decimal? ReturnAmount,
    decimal? Roi);
