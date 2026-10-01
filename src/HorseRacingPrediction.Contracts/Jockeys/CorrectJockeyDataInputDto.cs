namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record CorrectJockeyDataInputDto(string? DisplayName, string? NormalizedName, string? AffiliationCode, string? Reason);
