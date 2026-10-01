namespace HorseRacingPrediction.Contracts.Races;

public sealed record PredictionEvaluationDto(DateTimeOffset EvaluatedAt, int EvaluationRevision,
    IReadOnlyList<string> HitTypeCodes, decimal? ScoreSummary, decimal? ReturnAmount, decimal? Roi);
