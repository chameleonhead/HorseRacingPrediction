namespace HorseRacingPrediction.Api.Contracts;

public sealed record UpdateTrainerProfileRequest(
    string? DisplayName,
    string? NormalizedName,
    string? AffiliationCode);
