using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class PayoutResultDeclared : AggregateEvent<RaceAggregate, RaceId>
{
    public PayoutResultDeclared(DateTimeOffset declaredAt,
        IReadOnlyList<PayoutEntry>? winPayouts = null,
        IReadOnlyList<PayoutEntry>? placePayouts = null,
        IReadOnlyList<PayoutEntry>? quinellaPayouts = null,
        IReadOnlyList<PayoutEntry>? exactaPayouts = null,
        IReadOnlyList<PayoutEntry>? trifectaPayouts = null)
    {
        DeclaredAt = declaredAt;
        WinPayouts = winPayouts ?? Array.Empty<PayoutEntry>();
        PlacePayouts = placePayouts ?? Array.Empty<PayoutEntry>();
        QuinellaPayouts = quinellaPayouts ?? Array.Empty<PayoutEntry>();
        ExactaPayouts = exactaPayouts ?? Array.Empty<PayoutEntry>();
        TrifectaPayouts = trifectaPayouts ?? Array.Empty<PayoutEntry>();
    }

    public DateTimeOffset DeclaredAt { get; }
    public IReadOnlyList<PayoutEntry> WinPayouts { get; }
    public IReadOnlyList<PayoutEntry> PlacePayouts { get; }
    public IReadOnlyList<PayoutEntry> QuinellaPayouts { get; }
    public IReadOnlyList<PayoutEntry> ExactaPayouts { get; }
    public IReadOnlyList<PayoutEntry> TrifectaPayouts { get; }
}
