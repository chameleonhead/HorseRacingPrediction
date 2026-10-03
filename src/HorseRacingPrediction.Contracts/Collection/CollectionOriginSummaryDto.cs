namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionOriginSummaryDto(
    CollectionResourceKeyDto Resource,
    string? Name,
    string? Reason,
    string? DetailUrl,
    bool DetailsAvailable,
    string? DetailUnavailableReason = null);
