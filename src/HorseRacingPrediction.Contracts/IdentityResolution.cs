namespace HorseRacingPrediction.Contracts;

public sealed record ResolveHorseIdentityRequest(string Name, string? SourceIdentity = null, DateOnly? BirthDate = null);
public sealed record ResolveRaceIdentityRequest(DateOnly Date, string Course, int Number);
public sealed record ResolvedIdentityDto(string Id);
