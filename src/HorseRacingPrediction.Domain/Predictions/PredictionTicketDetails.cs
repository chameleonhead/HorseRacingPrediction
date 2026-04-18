namespace HorseRacingPrediction.Domain.Predictions;

public sealed record PredictionTicketDetails(
    string PredictionTicketId,
    string? RaceId,
    string? PredictorType,
    string? PredictorId,
    decimal ConfidenceScore,
    string? SummaryComment,
    DateTimeOffset? PredictedAt,
    TicketStatus TicketStatus,
    IReadOnlyCollection<PredictionMarkDetails> Marks,
    IReadOnlyCollection<BettingSuggestionDetails> BettingSuggestions,
    IReadOnlyCollection<PredictionRationaleDetails> Rationales,
    IReadOnlyCollection<PredictionEvaluationDetails> Evaluations);
