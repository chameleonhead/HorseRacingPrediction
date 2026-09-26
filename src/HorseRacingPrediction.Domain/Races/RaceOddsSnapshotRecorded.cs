using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed record RaceOddsEntry(int HorseNumber, decimal WinOdds, int? Popularity = null);
public sealed record RaceOddsObservation(string Market, string Selection, decimal Value,
    int? Popularity = null);
public sealed record RaceOddsAssignment(int HorseNumber, string HorseId, string EntryId, int? GateNumber);

public static class RaceOddsSelection
{
    public static bool CanResolve(RaceOddsObservation observation, IReadOnlyList<RaceOddsAssignment> assignments)
    {
        var market = observation.Market.Trim().ToUpperInvariant();
        var expectedCount = market switch
        {
            "WIN" or "PLACE" => 1,
            "QUINELLA" or "EXACTA" or "WIDE" or "BRACKETQUINELLA" => 2,
            "TRIO" or "TRIFECTA" => 3,
            _ => 0,
        };
        // Unknown markets remain opaque observations; do not guess their selection semantics.
        if (expectedCount == 0) return true;
        var selections = observation.Selection.Split('-', StringSplitOptions.TrimEntries);
        if (selections.Length != expectedCount) return false;
        var isFrame = market == "BRACKETQUINELLA";
        if (isFrame && assignments.Any(entry => entry.GateNumber is null)) return false;
        return selections.All(selection => int.TryParse(selection, out var number) && number > 0
            && assignments.Any(entry => isFrame ? entry.GateNumber == number : entry.HorseNumber == number));
    }
}

public sealed class RaceOddsSnapshotRecorded(DateTimeOffset observedAt, IReadOnlyList<RaceOddsEntry> entries,
    IReadOnlyList<RaceOddsObservation>? observations = null,
    IReadOnlyList<RaceOddsAssignment>? assignments = null)
    : AggregateEvent<RaceAggregate, RaceId>
{
    public DateTimeOffset ObservedAt { get; } = observedAt;
    public IReadOnlyList<RaceOddsEntry> Entries { get; } = entries;
    public IReadOnlyList<RaceOddsAssignment> Assignments { get; } = assignments ?? [];
    public IReadOnlyList<RaceOddsObservation> Observations { get; } = observations
        ?? entries.Select(x => new RaceOddsObservation("Win", x.HorseNumber.ToString(), x.WinOdds,
            x.Popularity)).ToArray();
}
