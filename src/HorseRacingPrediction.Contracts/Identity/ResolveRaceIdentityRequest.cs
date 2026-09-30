
namespace HorseRacingPrediction.Contracts.Identity;

public sealed record ResolveRaceIdentityRequest(DateOnly Date, string Course, int Number);
