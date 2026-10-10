namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;

public readonly record struct CollectionScheduleCursor(
    DateTimeOffset NextCollectionAt, long ResourcePk, string DefinitionId);

public sealed record CollectionScheduleSweepDecision(
    bool ShouldCollect, string Reason, CollectionScheduleCursor NextCursor);

public sealed record CollectionScheduleSweepProgress(
    DateTimeOffset? Horizon, CollectionScheduleCursor? Cursor);

public static class CollectionScheduleSweepPolicy
{
    public static CollectionScheduleCursor CursorAfter(CollectionScheduleCandidate candidate)
    {
        if (candidate.State.NextCollectionAt is not { } dueAt)
            throw new ArgumentException("A due candidate must have a next-collection time.", nameof(candidate));

        return new(dueAt, candidate.ResourcePk, candidate.State.Definition.Value);
    }

    public static CollectionScheduleSweepDecision Decide(
        CollectionScheduleCandidate candidate, CollectionSchedule schedule, DateTimeOffset now,
        bool isRepairHeld = false)
    {
        var cursor = CursorAfter(candidate);
        var reason = candidate.State.Status switch
        {
            CollectionStateStatus.Failed => "failed",
            CollectionStateStatus.Collecting => "collecting",
            _ when candidate.State.NextCollectionAt > now => "no-longer-due",
            _ when candidate.HasActiveTask => "active",
            _ when isRepairHeld => "repair-held",
            _ when !schedule.ShouldCollect => schedule.Reason,
            _ => null,
        };

        return reason is null
            ? new(true, "eligible", cursor)
            : new(false, reason, cursor);
    }

    public static CollectionScheduleSweepProgress CompletePage(
        CollectionScheduleSweepProgress progress, int candidateCount, int pageSize)
    {
        if (candidateCount < 0) throw new ArgumentOutOfRangeException(nameof(candidateCount));
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
        return candidateCount < pageSize ? new(null, null) : progress;
    }
}
