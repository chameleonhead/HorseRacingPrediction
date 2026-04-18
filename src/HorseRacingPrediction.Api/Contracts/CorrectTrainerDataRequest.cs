namespace HorseRacingPrediction.Api.Contracts;

public sealed record CorrectTrainerDataRequest(
    string? DisplayName,
    string? NormalizedName,
    string? AffiliationCode,
    string? Reason);
