
namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record UpdateJockeyProfileRequest(
    string? DisplayName,
    string? NormalizedName,
    string? AffiliationCode);
