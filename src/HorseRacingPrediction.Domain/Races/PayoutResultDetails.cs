namespace HorseRacingPrediction.Domain.Races;

public sealed record PayoutResultDetails(
    DateTimeOffset DeclaredAt,
    IReadOnlyList<PayoutEntry> WinPayouts,
    IReadOnlyList<PayoutEntry> PlacePayouts,
    IReadOnlyList<PayoutEntry> QuinellaPayouts,
    IReadOnlyList<PayoutEntry> ExactaPayouts,
    IReadOnlyList<PayoutEntry> TrifectaPayouts);
