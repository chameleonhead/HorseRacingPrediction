namespace HorseRacingPrediction.Contracts.Identity;

public sealed record ResolveRaceIdentityInputDto(DateOnly Date, string Course, int Number);
