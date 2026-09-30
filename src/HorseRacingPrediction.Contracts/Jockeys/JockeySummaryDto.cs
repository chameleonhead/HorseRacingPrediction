
namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record JockeySummaryDto(
    string JockeyId,
    string DisplayName,
    string NormalizedName,
    string? AffiliationCode,
    int AliasCount);
