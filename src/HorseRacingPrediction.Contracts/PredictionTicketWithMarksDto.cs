namespace HorseRacingPrediction.Contracts;

public sealed record PredictionTicketWithMarksDto(
    string PredictionTicketId,
    string? RaceId,
    string? PredictorType,
    string? PredictorId,
    decimal ConfidenceScore,
    string? SummaryComment,
    DateTimeOffset? PredictedAt,
    IReadOnlyList<PredictionMarkEntryDto> Marks);
