namespace HorseRacingPrediction.Api.Contracts;

public sealed record JockeyProfileResponse(
    string JockeyId,
    string DisplayName,
    string NormalizedName,
    string? AffiliationCode,
    IReadOnlyList<AliasResponse> Aliases);
