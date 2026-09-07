using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts;

public sealed record DeclarePayoutResultRequest(
    [property: Required] DateTimeOffset DeclaredAt,
    IReadOnlyList<PayoutEntryDto>? WinPayouts,
    IReadOnlyList<PayoutEntryDto>? PlacePayouts,
    IReadOnlyList<PayoutEntryDto>? QuinellaPayouts,
    IReadOnlyList<PayoutEntryDto>? ExactaPayouts,
    IReadOnlyList<PayoutEntryDto>? TrifectaPayouts);
