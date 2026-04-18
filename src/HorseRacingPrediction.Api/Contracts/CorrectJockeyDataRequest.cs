namespace HorseRacingPrediction.Api.Contracts;

public sealed record CorrectJockeyDataRequest(
    string? DisplayName,
    string? NormalizedName,
    string? AffiliationCode,
    string? Reason);
