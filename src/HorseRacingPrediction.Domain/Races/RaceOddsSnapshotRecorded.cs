using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed record RaceOddsEntry(int HorseNumber, decimal WinOdds, int? Popularity = null);

public sealed class RaceOddsSnapshotRecorded(DateTimeOffset observedAt, IReadOnlyList<RaceOddsEntry> entries)
    : AggregateEvent<RaceAggregate, RaceId>
{
    public DateTimeOffset ObservedAt { get; } = observedAt;
    public IReadOnlyList<RaceOddsEntry> Entries { get; } = entries;
}
