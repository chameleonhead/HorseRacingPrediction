using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceClosed : AggregateEvent<RaceAggregate, RaceId>
{
}
