using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class CorrectPredictionMetadataCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public CorrectPredictionMetadataCommand(PredictionTicketId aggregateId,
        decimal? confidenceScore = null, string? summaryComment = null, string? reason = null)
        : base(aggregateId)
    {
        ConfidenceScore = confidenceScore;
        SummaryComment = summaryComment;
        Reason = reason;
    }

    public decimal? ConfidenceScore { get; }
    public string? SummaryComment { get; }
    public string? Reason { get; }
}
