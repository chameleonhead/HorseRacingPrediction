using HorseRacingPrediction.Contracts;

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
    int MarkCount,
    string? RaceName = null,
    DateOnly? RaceDate = null,
    string? RacecourseCode = null,
    int? RaceNumber = null,
    string? PrimaryHorseName = null);
