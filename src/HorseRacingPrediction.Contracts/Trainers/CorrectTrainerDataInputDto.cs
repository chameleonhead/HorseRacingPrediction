namespace HorseRacingPrediction.Contracts.Trainers;

public sealed record CorrectTrainerDataInputDto(string? DisplayName, string? NormalizedName, string? AffiliationCode, string? Reason);
