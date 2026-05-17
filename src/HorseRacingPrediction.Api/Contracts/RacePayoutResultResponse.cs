namespace HorseRacingPrediction.Api.Contracts;

public sealed record RacePayoutResultResponse(
    DateTimeOffset DeclaredAt,
    IReadOnlyList<RacePayoutEntryResponse> WinPayouts,
    IReadOnlyList<RacePayoutEntryResponse> PlacePayouts,
    IReadOnlyList<RacePayoutEntryResponse> QuinellaPayouts,
    IReadOnlyList<RacePayoutEntryResponse> ExactaPayouts,
    IReadOnlyList<RacePayoutEntryResponse> TrifectaPayouts);
