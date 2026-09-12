using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed record RaceOddsEntry(int HorseNumber, decimal WinOdds, int? Popularity = null);
public sealed record RaceOddsObservation(string Market, string Selection, decimal Value,
    int? Popularity = null);

public sealed class RaceOddsSnapshotRecorded(DateTimeOffset observedAt, IReadOnlyList<RaceOddsEntry> entries,
    IReadOnlyList<RaceOddsObservation>? observations = null)
    : AggregateEvent<RaceAggregate, RaceId>
{
    public DateTimeOffset ObservedAt { get; } = observedAt;
    public IReadOnlyList<RaceOddsEntry> Entries { get; } = entries;
    public IReadOnlyList<RaceOddsObservation> Observations { get; } = observations
        ?? entries.Select(x => new RaceOddsObservation("Win", x.HorseNumber.ToString(), x.WinOdds,
            x.Popularity)).ToArray();
}
