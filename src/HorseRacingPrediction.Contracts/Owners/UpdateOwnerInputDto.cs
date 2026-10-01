namespace HorseRacingPrediction.Contracts.Owners;

public sealed record UpdateOwnerInputDto(string DisplayName, string Reason, IReadOnlyList<string>? NameVariants = null);
