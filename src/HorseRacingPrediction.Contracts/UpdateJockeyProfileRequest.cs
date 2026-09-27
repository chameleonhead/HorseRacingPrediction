namespace HorseRacingPrediction.Contracts;

public sealed record UpdateJockeyProfileRequest(
    string? DisplayName,
    string? NormalizedName,
    string? AffiliationCode);
