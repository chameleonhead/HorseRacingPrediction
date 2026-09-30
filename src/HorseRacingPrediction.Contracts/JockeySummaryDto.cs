namespace HorseRacingPrediction.Contracts;

public sealed record JockeySummaryDto(
    string JockeyId,
    string DisplayName,
    string NormalizedName,
    string? AffiliationCode,
    int AliasCount);