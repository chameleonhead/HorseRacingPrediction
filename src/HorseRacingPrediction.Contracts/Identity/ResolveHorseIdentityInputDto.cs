namespace HorseRacingPrediction.Contracts.Identity;

public sealed record ResolveHorseIdentityInputDto(
    string Name,
    string? SourceIdentity = null,
    DateOnly? BirthDate = null);
