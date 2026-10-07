using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.CollectionController;

internal sealed record CollectionRuntimeActionSnapshot(
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
    int ConsecutiveErrors,
    long CurrentCycleToken,
    long? ActiveCycleStartTimestamp);

internal readonly record struct CollectionRuntimeCycleToken(CollectionRuntimeAction Action, long Value);

internal readonly record struct CollectionRuntimeCycleCounts(
    int Inspected = 0,
    int Created = 0,
    int Reclaimed = 0,
    int Sent = 0,
    int Completed = 0)
{
    public bool IsValid => Inspected >= 0 && Created >= 0 && Reclaimed >= 0 && Sent >= 0 && Completed >= 0;
}

internal static class CollectionRuntimeStatusTransitionPolicy
{
    public static CollectionRuntimeActionSnapshot Begin(CollectionRuntimeActionSnapshot current,
        long token, DateTimeOffset startedAtUtc, long startedTimestamp)
    {
        if (!current.Enabled || token <= current.CurrentCycleToken) return current;

        return current with
        {
            State = CollectionRuntimeState.Running,
            Reason = null,
            LastStartedAtUtc = startedAtUtc,
            CurrentCycleToken = token,
            ActiveCycleStartTimestamp = startedTimestamp,
        };
    }

    public static CollectionRuntimeActionSnapshot Complete(CollectionRuntimeActionSnapshot current,
        long token, DateTimeOffset completedAtUtc, long durationMilliseconds,
        CollectionRuntimeReason? reason, CollectionRuntimeCycleCounts counts)
    {
        if (!IsCurrentCycle(current, token) || reason == CollectionRuntimeReason.Error || !counts.IsValid)
            return current;

        return current with
        {
            State = CollectionRuntimeState.Waiting,
            Reason = reason,
            LastCompletedAtUtc = completedAtUtc,
            LastSuccessfulCycleAtUtc = completedAtUtc,
            LastDurationMilliseconds = Math.Max(0, durationMilliseconds),
            InspectedCount = counts.Inspected,
            CreatedCount = counts.Created,
            ReclaimedCount = counts.Reclaimed,
            SentCount = counts.Sent,
            CompletedCount = counts.Completed,
            ConsecutiveErrors = 0,
            ActiveCycleStartTimestamp = null,
        };
    }

    public static CollectionRuntimeActionSnapshot Cancel(CollectionRuntimeActionSnapshot current,
        long token, DateTimeOffset completedAtUtc, long durationMilliseconds,
        CollectionRuntimeCycleCounts counts)
    {
        if (!IsCurrentCycle(current, token) || !counts.IsValid) return current;

        return current with
        {
            State = CollectionRuntimeState.Waiting,
            Reason = null,
            LastCompletedAtUtc = completedAtUtc,
            LastDurationMilliseconds = Math.Max(0, durationMilliseconds),
            InspectedCount = counts.Inspected,
            CreatedCount = counts.Created,
            ReclaimedCount = counts.Reclaimed,
            SentCount = counts.Sent,
            CompletedCount = counts.Completed,
            ActiveCycleStartTimestamp = null,
        };
    }

    public static CollectionRuntimeActionSnapshot Fail(CollectionRuntimeActionSnapshot current,
        long token, DateTimeOffset completedAtUtc, long durationMilliseconds,
        CollectionRuntimeCycleCounts counts)
    {
        if (!IsCurrentCycle(current, token) || !counts.IsValid) return current;

        return current with
        {
            State = CollectionRuntimeState.Error,
            Reason = CollectionRuntimeReason.Error,
            LastCompletedAtUtc = completedAtUtc,
            LastDurationMilliseconds = Math.Max(0, durationMilliseconds),
            InspectedCount = counts.Inspected,
            CreatedCount = counts.Created,
            ReclaimedCount = counts.Reclaimed,
            SentCount = counts.Sent,
            CompletedCount = counts.Completed,
            ConsecutiveErrors = current.ConsecutiveErrors + 1,
            ActiveCycleStartTimestamp = null,
        };
    }

    public static CollectionRuntimeActionSnapshot RecordProgress(CollectionRuntimeActionSnapshot current,
        long token, DateTimeOffset progressedAtUtc)
    {
        if (!IsCurrentCycle(current, token)) return current;
        return current with { LastProgressAtUtc = progressedAtUtc };
    }

    private static bool IsCurrentCycle(CollectionRuntimeActionSnapshot current, long token) =>
        current.State == CollectionRuntimeState.Running && current.CurrentCycleToken == token
        && current.ActiveCycleStartTimestamp is not null;
}
