namespace HorseRacingPrediction.Api.Contracts;

public sealed record UpdateJockeyProfileRequest(
    string? DisplayName,
    string? NormalizedName,
    string? AffiliationCode);
