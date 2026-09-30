
namespace HorseRacingPrediction.Contracts.Owners;

public sealed record UpdateOwnerRequest(string DisplayName, string Reason, IReadOnlyList<string>? NameVariants = null);
