namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record PayoutResultSnapshot(
    DateTimeOffset DeclaredAt,
    IReadOnlyList<PayoutEntrySnapshot> WinPayouts,
    IReadOnlyList<PayoutEntrySnapshot> PlacePayouts,
    IReadOnlyList<PayoutEntrySnapshot> QuinellaPayouts,
    IReadOnlyList<PayoutEntrySnapshot> ExactaPayouts,
    IReadOnlyList<PayoutEntrySnapshot> TrifectaPayouts);
