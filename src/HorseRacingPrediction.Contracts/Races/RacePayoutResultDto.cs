
namespace HorseRacingPrediction.Contracts.Races;

public sealed record RacePayoutResultDto(
    DateTimeOffset DeclaredAt,
    IReadOnlyList<RacePayoutEntryDto> WinPayouts,
    IReadOnlyList<RacePayoutEntryDto> PlacePayouts,
    IReadOnlyList<RacePayoutEntryDto> QuinellaPayouts,
    IReadOnlyList<RacePayoutEntryDto> ExactaPayouts,
    IReadOnlyList<RacePayoutEntryDto> TrifectaPayouts,
    IReadOnlyList<RacePayoutEntryDto>? BracketQuinellaPayouts = null,
    IReadOnlyList<RacePayoutEntryDto>? WidePayouts = null,
    IReadOnlyList<RacePayoutEntryDto>? TrioPayouts = null);
