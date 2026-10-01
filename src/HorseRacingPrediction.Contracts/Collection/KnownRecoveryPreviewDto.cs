namespace HorseRacingPrediction.Contracts.Collection;

public sealed record KnownRecoveryPreviewDto(string RecipeId, int MatchingFindingCount, int CanaryLimit,
    bool RecoveryEnabled, bool MaintenanceMode, bool SafeToApply, string? BlockingReason);
