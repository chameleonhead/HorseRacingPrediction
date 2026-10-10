using Microsoft.EntityFrameworkCore;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed partial class CollectionPlatformStore
{
    public async Task<IReadOnlyList<CollectionScheduleCandidate>> GetDueScheduleCandidatesAsync(
        DateTimeOffset horizon, CollectionScheduleCursor? after = null, int limit = 500,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var dueWindow = from state in db.States.AsNoTracking()
                        join item in db.Resources.AsNoTracking() on state.ResourcePk equals item.ResourcePk
                        where state.Status != CollectionStateStatus.Collecting
                            && state.Status != CollectionStateStatus.Failed
                            && state.NextCollectionAt != null
                            && state.NextCollectionAt <= horizon
                            && !db.ActiveTasks.Any(active => active.ResourcePk == state.ResourcePk
                                && active.DefinitionId == state.DefinitionId)
                        select new { state, item };

        if (after is { } cursor)
        {
            dueWindow = dueWindow.Where(x => x.state.NextCollectionAt > cursor.NextCollectionAt
                || (x.state.NextCollectionAt == cursor.NextCollectionAt
                    && (x.state.ResourcePk > cursor.ResourcePk
                        || (x.state.ResourcePk == cursor.ResourcePk
                            && string.Compare(x.state.DefinitionId, cursor.DefinitionId) > 0))));
        }

        return await dueWindow.OrderBy(x => x.state.NextCollectionAt)
            .ThenBy(x => x.state.ResourcePk)
            .ThenBy(x => x.state.DefinitionId)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(x => new CollectionScheduleCandidate(
                new CollectionStateSnapshot(
                    new(x.item.Type, x.item.Provider, x.item.ResourceId), new(x.state.DefinitionId),
                    x.state.AppliedRevision, x.state.RequiredRevision, x.state.LastCollectedAt,
                    x.state.NextCollectionAt, x.state.Status),
                false, x.state.ResourcePk))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Compatibility wrapper; callers needing actual durable task creation should use the detailed result.</summary>
    public async Task<CollectionScheduleSweepDecision> TryScheduleDueAsync(
        CollectionScheduleCandidate candidate, CollectionSchedule scheduleFromSnapshot, DateTimeOffset now,
        ICollectionSchedulePolicy policy,
        CancellationToken cancellationToken = default)
        => (await TryScheduleDueDetailedAsync(candidate, scheduleFromSnapshot, now, policy, cancellationToken)
            .ConfigureAwait(false)).Decision;

    public async Task<CollectionScheduleAttemptResult> TryScheduleDueDetailedAsync(
        CollectionScheduleCandidate candidate, CollectionSchedule scheduleFromSnapshot, DateTimeOffset now,
        ICollectionSchedulePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var pageCursor = CollectionScheduleSweepPolicy.CursorAfter(candidate);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var current = await (from state in db.States
                                 join item in db.Resources on state.ResourcePk equals item.ResourcePk
                                 where state.ResourcePk == candidate.ResourcePk
                                     && state.DefinitionId == candidate.State.Definition.Value
                                 select new { state, item }).SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            CollectionScheduleSweepDecision decision;
            CollectionRequestReceipt? receipt = null;
            if (current is null)
            {
                decision = new(false, "missing", pageCursor);
            }
            else
            {
                var snapshot = new CollectionStateSnapshot(
                    new(current.item.Type, current.item.Provider, current.item.ResourceId),
                    new(current.state.DefinitionId), current.state.AppliedRevision, current.state.RequiredRevision,
                    current.state.LastCollectedAt, current.state.NextCollectionAt, current.state.Status);
                var currentCandidate = candidate with { State = snapshot };
                var active = await db.ActiveTasks.AnyAsync(x => x.ResourcePk == current.state.ResourcePk
                    && x.DefinitionId == current.state.DefinitionId, cancellationToken).ConfigureAwait(false);
                currentCandidate = currentCandidate with { HasActiveTask = active };
                var schedule = snapshot == candidate.State
                    ? scheduleFromSnapshot
                    : policy.Evaluate(snapshot.Resource, snapshot, now);
                var held = await IsRepairHeldAsync(db, current.state.ResourcePk, cancellationToken)
                    .ConfigureAwait(false);
                decision = current.state.NextCollectionAt is null
                    ? new(false, "no-longer-due", pageCursor)
                    : CollectionScheduleSweepPolicy.Decide(currentCandidate, schedule, now, held);
                decision = decision with { NextCursor = pageCursor };

                if (decision.ShouldCollect)
                {
                    receipt = await RequestCoreAsync(db, snapshot.Resource, snapshot.Definition, snapshot.RequiredRevision,
                        CollectionReason.ScheduledRefresh, now, schedule.Lane, (int)schedule.Priority,
                        explicitUrl: null, batchId: null, effectiveDate: null, attributes: null,
                        payloadFingerprint: null, cancellationToken).ConfigureAwait(false);
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(decision, receipt);
        }
        finally { _gate.Release(); }
    }
}
