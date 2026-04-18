namespace HorseRacingPrediction.Api.Contracts;

public sealed record PredictionTicketResponse(
    string PredictionTicketId,
    string? RaceId,
    string? PredictorType,
    string? PredictorId,
    decimal ConfidenceScore,
    string? SummaryComment,
    DateTimeOffset? PredictedAt,
    IReadOnlyCollection<PredictionMarkResponse> Marks);
