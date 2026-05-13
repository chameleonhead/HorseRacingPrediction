using HorseRacingPrediction.Agents.Contracts;

namespace HorseRacingPrediction.Api.Contracts;

public sealed class SearchPredictionTicketsRequest
{
    public string? PredictionTicketId { get; init; }
    public string? RaceId { get; init; }
    public string? PredictorType { get; init; }
    public string? PredictorId { get; init; }
    public TicketStatus? TicketStatus { get; init; }
    public EvaluationStatus? EvaluationStatus { get; init; }
    public DateTimeOffset? PredictedAtFrom { get; init; }
    public DateTimeOffset? PredictedAtTo { get; init; }
    public decimal? MinConfidenceScore { get; init; }
    public decimal? MaxConfidenceScore { get; init; }
    public string? SummaryComment { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? SortBy { get; init; }
    public bool? SortDescending { get; init; }
}