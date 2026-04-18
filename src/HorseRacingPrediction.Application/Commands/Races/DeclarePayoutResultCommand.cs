using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class DeclarePayoutResultCommand : Command<RaceAggregate, RaceId>
{
    public DeclarePayoutResultCommand(RaceId aggregateId, DateTimeOffset declaredAt,
        IReadOnlyList<PayoutEntry>? winPayouts = null,
        IReadOnlyList<PayoutEntry>? placePayouts = null,
        IReadOnlyList<PayoutEntry>? quinellaPayouts = null,
        IReadOnlyList<PayoutEntry>? exactaPayouts = null,
        IReadOnlyList<PayoutEntry>? trifectaPayouts = null)
        : base(aggregateId)
    {
        DeclaredAt = declaredAt;
        WinPayouts = winPayouts;
        PlacePayouts = placePayouts;
        QuinellaPayouts = quinellaPayouts;
        ExactaPayouts = exactaPayouts;
        TrifectaPayouts = trifectaPayouts;
    }

    public DateTimeOffset DeclaredAt { get; }
    public IReadOnlyList<PayoutEntry>? WinPayouts { get; }
    public IReadOnlyList<PayoutEntry>? PlacePayouts { get; }
    public IReadOnlyList<PayoutEntry>? QuinellaPayouts { get; }
    public IReadOnlyList<PayoutEntry>? ExactaPayouts { get; }
    public IReadOnlyList<PayoutEntry>? TrifectaPayouts { get; }
}
