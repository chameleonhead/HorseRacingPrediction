namespace HorseRacingPrediction.Contracts.Collection;

public sealed record BackfillBatchRecoveryDto(CollectionBatchKind Kind,
    CollectionBatchRecoveryState RecoveryState, DateOnly? NextDate, string? ReviewReason);
