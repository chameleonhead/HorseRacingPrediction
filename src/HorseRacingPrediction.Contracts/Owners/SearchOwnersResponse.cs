namespace HorseRacingPrediction.Contracts.Owners;

public sealed record SearchOwnersResponse(IReadOnlyList<OwnerSummaryDto> Owners);
