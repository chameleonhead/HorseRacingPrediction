namespace HorseRacingPrediction.Contracts.Trainers;

public sealed record UpdateTrainerProfileInputDto(string? DisplayName, string? NormalizedName, string? AffiliationCode);
