namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record PayoutResultSnapshot(
    DateTimeOffset DeclaredAt,
    List<PayoutEntrySnapshot> WinPayouts,
    List<PayoutEntrySnapshot> PlacePayouts,
    List<PayoutEntrySnapshot> QuinellaPayouts,
    List<PayoutEntrySnapshot> ExactaPayouts,
    List<PayoutEntrySnapshot> TrifectaPayouts);
