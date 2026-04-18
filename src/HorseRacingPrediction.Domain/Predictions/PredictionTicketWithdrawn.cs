using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public sealed class PredictionTicketWithdrawn : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionTicketWithdrawn(string? reason = null)
    {
        Reason = reason;
    }

    public string? Reason { get; }
}
