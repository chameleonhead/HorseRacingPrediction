namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionRuntimeActionStatusDto(
    CollectionRuntimeAction Action,
    bool Enabled,
    TimeSpan? EffectiveInterval,
    CollectionRuntimeState State,
    CollectionRuntimeReason? Reason,
    DateTimeOffset? LastStartedAtUtc,
    DateTimeOffset? LastCompletedAtUtc,
    DateTimeOffset? LastSuccessfulCycleAtUtc,
    DateTimeOffset? LastProgressAtUtc,
    long? LastDurationMilliseconds,
    int InspectedCount,
    int CreatedCount,
    int ReclaimedCount,
    int SentCount,
    int CompletedCount,
    int ConsecutiveErrors);
