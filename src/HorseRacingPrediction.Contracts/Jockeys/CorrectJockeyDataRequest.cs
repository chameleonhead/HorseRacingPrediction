
namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record CorrectJockeyDataRequest(
    string? DisplayName,
    string? NormalizedName,
    string? AffiliationCode,
    string? Reason);
