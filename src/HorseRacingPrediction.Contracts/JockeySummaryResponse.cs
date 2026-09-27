namespace HorseRacingPrediction.Contracts;

public sealed record JockeySummaryResponse(
    string JockeyId,
    string DisplayName,
    string NormalizedName,
    string? AffiliationCode,
    int AliasCount);