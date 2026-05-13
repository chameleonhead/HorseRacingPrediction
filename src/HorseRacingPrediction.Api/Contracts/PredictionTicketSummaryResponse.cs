using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record PredictionTicketSummaryResponse(
    string PredictionTicketId,
    string? RaceId,
    string? PredictorType,
    string? PredictorId,
    decimal ConfidenceScore,
    string? SummaryComment,
    DateTimeOffset? PredictedAt,
    TicketStatus TicketStatus,
    EvaluationStatus EvaluationStatus,
    int MarkCount);