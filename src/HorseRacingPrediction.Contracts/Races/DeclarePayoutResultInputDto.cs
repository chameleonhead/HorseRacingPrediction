using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record DeclarePayoutResultInputDto(
    [property: Required] DateTimeOffset DeclaredAt,
    IReadOnlyList<PayoutEntryDto>? WinPayouts,
    IReadOnlyList<PayoutEntryDto>? PlacePayouts,
    IReadOnlyList<PayoutEntryDto>? QuinellaPayouts,
    IReadOnlyList<PayoutEntryDto>? ExactaPayouts,
    IReadOnlyList<PayoutEntryDto>? TrifectaPayouts,
    IReadOnlyList<PayoutEntryDto>? BracketQuinellaPayouts = null,
    IReadOnlyList<PayoutEntryDto>? WidePayouts = null,
    IReadOnlyList<PayoutEntryDto>? TrioPayouts = null);
