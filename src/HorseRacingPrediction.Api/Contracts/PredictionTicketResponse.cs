using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record PredictionTicketResponse(
    string PredictionTicketId,
    string? RaceId,
    string? PredictorType,
    string? PredictorId,
    decimal ConfidenceScore,
    string? SummaryComment,
    DateTimeOffset? PredictedAt,
    IReadOnlyCollection<PredictionMarkResponse> Marks,
    TicketStatus TicketStatus = TicketStatus.Draft,
    EvaluationStatus EvaluationStatus = EvaluationStatus.Ready,
    string? RaceName = null,
    DateOnly? RaceDate = null,
    string? RacecourseCode = null,
    int? RaceNumber = null);
