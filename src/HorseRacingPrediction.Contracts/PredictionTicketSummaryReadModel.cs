namespace HorseRacingPrediction.Contracts;

public sealed record PredictionTicketSummaryReadModel(
    string PredictionTicketId,
    string? RaceId,
    string? PredictorType,
    string? PredictorId,
    decimal ConfidenceScore,
    string? SummaryComment,
    DateTimeOffset? PredictedAt,
    IReadOnlyList<PredictionMarkEntry> Marks);
