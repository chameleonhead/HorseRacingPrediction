using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class RaceCardPublished : AggregateEvent<RaceAggregate, RaceId>
{
    public RaceCardPublished(int entryCount)
    {
        EntryCount = entryCount;
    }

    public int EntryCount { get; }
}
