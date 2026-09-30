using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Contracts;

public sealed record PredictionTicketDto(
    string PredictionTicketId,
    string? RaceId,
    string? PredictorType,
    string? PredictorId,
    decimal ConfidenceScore,
    string? SummaryComment,
    DateTimeOffset? PredictedAt,
    IReadOnlyCollection<PredictionMarkDto> Marks,
    TicketStatus TicketStatus = TicketStatus.Draft,
    EvaluationStatus EvaluationStatus = EvaluationStatus.Ready,
    string? RaceName = null,
    DateOnly? RaceDate = null,
    string? RacecourseCode = null,
    int? RaceNumber = null);
