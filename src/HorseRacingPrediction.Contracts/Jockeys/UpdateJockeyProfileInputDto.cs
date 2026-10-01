namespace HorseRacingPrediction.Contracts.Jockeys;

public sealed record UpdateJockeyProfileInputDto(string? DisplayName, string? NormalizedName, string? AffiliationCode);
