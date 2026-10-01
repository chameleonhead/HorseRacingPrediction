namespace HorseRacingPrediction.Contracts.Races;

public sealed record PredictionComparisonTicketDto(string PredictionTicketId, string PredictorType, string PredictorId,
    HorseRacingPrediction.Contracts.Predictions.TicketStatus Status, decimal ConfidenceScore,
    string? SummaryComment, DateTimeOffset PredictedAt, IReadOnlyList<PredictionMarkSnapshotDto> Marks,
    PredictionEvaluationDto? LatestEvaluation,
    HorseRacingPrediction.Contracts.Predictions.EvaluationStatus EvaluationStatus);
