using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public sealed class PredictionMetadataCorrected : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionMetadataCorrected(decimal? confidenceScore = null, string? summaryComment = null,
        string? reason = null)
    {
        ConfidenceScore = confidenceScore;
        SummaryComment = summaryComment;
        Reason = reason;
    }

    public decimal? ConfidenceScore { get; }
    public string? SummaryComment { get; }
    public string? Reason { get; }
}
