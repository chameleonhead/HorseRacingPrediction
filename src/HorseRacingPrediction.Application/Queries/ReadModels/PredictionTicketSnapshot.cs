using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record PredictionTicketSnapshot(
    string PredictionTicketId,
    string PredictorType,
    string PredictorId,
    TicketStatus Status,
    decimal ConfidenceScore,
    string? SummaryComment,
    DateTimeOffset PredictedAt,
    IReadOnlyList<PredictionMarkSnapshot> Marks,
    PredictionEvaluationSnapshot? LatestEvaluation,
    EvaluationStatus EvaluationStatus);
