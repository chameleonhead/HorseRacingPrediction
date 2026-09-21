using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceRescheduled(string replacementRaceId) : AggregateEvent<RaceAggregate, RaceId>
{
    public string ReplacementRaceId { get; } = replacementRaceId;
}
