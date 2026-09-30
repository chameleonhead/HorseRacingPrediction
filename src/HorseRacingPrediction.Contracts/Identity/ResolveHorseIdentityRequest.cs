
namespace HorseRacingPrediction.Contracts.Identity;

public sealed record ResolveHorseIdentityRequest(string Name, string? SourceIdentity = null, DateOnly? BirthDate = null);
