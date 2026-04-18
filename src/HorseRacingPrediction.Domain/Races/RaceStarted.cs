using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceStarted : AggregateEvent<RaceAggregate, RaceId>
{
}
