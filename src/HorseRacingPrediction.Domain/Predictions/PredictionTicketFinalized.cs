using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public sealed class PredictionTicketFinalized : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
}
