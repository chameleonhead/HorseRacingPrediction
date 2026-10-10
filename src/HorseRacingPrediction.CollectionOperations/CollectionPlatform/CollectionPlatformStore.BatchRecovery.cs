using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed partial class CollectionPlatformStore
{
    private const int LegacyBatchScanLimit = 10;
    private const int RecoveryBatchLimit = 5;

    private async Task<BackfillBatchEntity> EnsureTypedBatchAsync(CollectionPlatformDbContext db,
        string batchId, string provider, DateOnly from, DateOnly to, CollectionBatchKind kind,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await using var transaction = connection.BeginTransaction(deferred: false);
        db.Database.UseTransaction(transaction);
        var batch = await db.BackfillBatches.SingleOrDefaultAsync(x => x.BatchId == batchId, cancellationToken)
            .ConfigureAwait(false);
        var isNewBatch = batch is null;
        if (batch is null)
        {
            batch = new BackfillBatchEntity
            {
                BatchId = batchId,
                Provider = provider,
                From = from,
                To = to,
                CreatedAt = now
            };
            db.BackfillBatches.Add(batch);
        }
        else if (!IdentityMatches(batch, provider, from, to))
        {
            throw new CollectionBatchIdentityConflictException("BatchIdentityConflict");
        }

        var progress = await db.BatchRecoveryProgress.SingleOrDefaultAsync(x => x.BatchId == batchId,
            cancellationToken).ConfigureAwait(false);
        if (progress is null)
        {
            var classification = isNewBatch
                ? new CollectionBatchClassification(kind, null)
                : await ClassifyBatchAsync(db, batch, cancellationToken).ConfigureAwait(false);
            if (!classification.IsReady)
                throw new CollectionBatchIdentityConflictException("BatchKindUnclassifiable");
            if (classification.Kind != kind)
                throw new CollectionBatchIdentityConflictException("BatchKindConflict");
            var scan = await GetOrCreateScanAsync(db, cancellationToken).ConfigureAwait(false);
            progress = CreateProgress(batch, classification.Kind, classification.ReviewReason, scan.VisitSequence);
            db.BatchRecoveryProgress.Add(progress);
            scan.VisitSequence++;
        }
        else if (progress.Kind != kind || progress.Kind == CollectionBatchKind.Unknown)
        {
            throw new CollectionBatchIdentityConflictException("BatchKindConflict");
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch;
    }

    private static bool IdentityMatches(BackfillBatchEntity batch, string provider, DateOnly from, DateOnly to)
        => string.Equals(batch.Provider, provider, StringComparison.Ordinal)
            && batch.From == from && batch.To == to;

    private static CollectionBatchRecoveryProgressEntity CreateProgress(BackfillBatchEntity batch,
        CollectionBatchKind kind, string? reviewReason, long sequence)
        => new()
        {
            BatchId = batch.BatchId,
            Kind = kind,
            NextDate = kind == CollectionBatchKind.Unknown ? null : batch.From,
            LastVisitedSequence = sequence,
            ReviewReason = reviewReason
        };

    private async Task<CollectionBatchRecoveryScanEntity> GetOrCreateScanAsync(
        CollectionPlatformDbContext db, CancellationToken cancellationToken)
    {
        var scan = await db.BatchRecoveryScans.SingleOrDefaultAsync(x => x.ScanId == 1, cancellationToken)
            .ConfigureAwait(false);
        if (scan is not null) return scan;
        scan = new CollectionBatchRecoveryScanEntity { ScanId = 1, VisitSequence = 0 };
        db.BatchRecoveryScans.Add(scan);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return scan;
    }

    private async Task<CollectionBatchClassification> ClassifyBatchAsync(CollectionPlatformDbContext db,
        BackfillBatchEntity batch, CancellationToken cancellationToken)
    {
        var reasonCounts = await (from request in db.Requests.AsNoTracking()
                                  where request.BatchId == batch.BatchId
                                      && request.Reason != CollectionReason.Discovery
                                  group request by request.Reason into groupRows
                                  select new { Reason = groupRows.Key, Count = groupRows.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (reasonCounts.Count == 0)
            return CollectionBatchRecoveryPolicy.ClassifyAggregated([], null);
        if (reasonCounts.Count != 1 || reasonCounts[0].Reason is not
            (CollectionReason.Backfill or CollectionReason.PeriodRecollection))
            return CollectionBatchRecoveryPolicy.ClassifyAggregated(reasonCounts.Select(x =>
                new CollectionBatchReasonCount(x.Reason, x.Count)).ToArray(), null);

        var kind = reasonCounts[0].Reason == CollectionReason.Backfill
            ? CollectionBatchKind.Backfill : CollectionBatchKind.PeriodRecollection;
        var prefix = kind == CollectionBatchKind.Backfill ? "backfill:" : "recollection:";
        var offset = prefix.Length + 1;
        var invalid = await db.Database.SqlQueryRaw<long>("""
            SELECT COUNT(*) AS Value
            FROM collection_requests AS q
            JOIN collection_resources AS r ON r.ResourcePk = q.ResourcePk
            WHERE q.BatchId = {0} AND q.Reason = {1}
              AND (r.Type != {2} OR r.Provider != {3} OR q.DefinitionId != 'race-discovery'
                OR substr(r.ResourceId, 1, {4}) != {5}
                OR length(r.ResourceId) != {11}
                OR substr(r.ResourceId, {6}, 8) NOT GLOB '[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'
                OR date(substr(r.ResourceId, {6}, 4) || '-' || substr(r.ResourceId, {7}, 2) || '-' || substr(r.ResourceId, {8}, 2)) IS NULL
                OR date(substr(r.ResourceId, {6}, 4) || '-' || substr(r.ResourceId, {7}, 2) || '-' || substr(r.ResourceId, {8}, 2)) !=
                    substr(r.ResourceId, {6}, 4) || '-' || substr(r.ResourceId, {7}, 2) || '-' || substr(r.ResourceId, {8}, 2)
                OR (CASE WHEN json_valid(q.MetadataJson)
                    THEN COALESCE(json_extract(q.MetadataJson, '$.backfillDate'), '') ELSE '' END) !=
                    substr(r.ResourceId, {6}, 4) || '-' || substr(r.ResourceId, {7}, 2) || '-' || substr(r.ResourceId, {8}, 2)
                OR (substr(r.ResourceId, {6}, 4) || '-' || substr(r.ResourceId, {7}, 2) || '-' || substr(r.ResourceId, {8}, 2)) < {9}
                OR (substr(r.ResourceId, {6}, 4) || '-' || substr(r.ResourceId, {7}, 2) || '-' || substr(r.ResourceId, {8}, 2)) > {10})
            """, batch.BatchId, reasonCounts[0].Reason.ToString(), CollectionResourceType.Race.ToString(),
            batch.Provider, prefix.Length, prefix, offset, offset + 4, offset + 6,
            batch.From.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            batch.To.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), prefix.Length + 8)
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        return CollectionBatchRecoveryPolicy.ClassifyAggregated(reasonCounts.Select(x =>
            new CollectionBatchReasonCount(x.Reason, x.Count)).ToArray(),
            invalid == 0 ? null : "RootShapeOrRangeMismatch");
    }

    private async Task<CollectionRequestReceipt> ProcessManualBatchDateAsync(string batchId, DateOnly date,
        CollectionBatchKind kind, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            db.Database.UseTransaction(transaction);
            var batch = await db.BackfillBatches.SingleAsync(x => x.BatchId == batchId, cancellationToken)
                .ConfigureAwait(false);
            var progress = await db.BatchRecoveryProgress.SingleAsync(x => x.BatchId == batchId,
                cancellationToken).ConfigureAwait(false);
            if (progress.Kind != kind || progress.Kind == CollectionBatchKind.Unknown)
                throw new CollectionBatchIdentityConflictException("BatchKindConflict");

            var resourceId = $"{(kind == CollectionBatchKind.Backfill ? "backfill:" : "recollection:")}{date:yyyyMMdd}";
            CollectionRequestReceipt receipt;
            var existingState = kind == CollectionBatchKind.Backfill
                && await (from state in db.States.AsNoTracking()
                          join resource in db.Resources.AsNoTracking() on state.ResourcePk equals resource.ResourcePk
                          where resource.Type == CollectionResourceType.Race && resource.Provider == batch.Provider
                              && resource.ResourceId == resourceId && state.DefinitionId == "race-discovery"
                          select state.ResourcePk).AnyAsync(cancellationToken).ConfigureAwait(false);
            if (existingState)
            {
                receipt = new(Guid.Empty, null, false);
            }
            else
            {
                receipt = await RequestCoreAsync(db, new(CollectionResourceType.Race, batch.Provider, resourceId),
                    new("race-discovery"), 1,
                    kind == CollectionBatchKind.Backfill ? CollectionReason.Backfill : CollectionReason.PeriodRecollection,
                    now, CollectionLane.Background, (int)CollectionPriority.Background, null, batchId, date,
                    new Dictionary<string, string>
                    {
                        ["batchId"] = batchId,
                        ["backfillDate"] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                    }, null, cancellationToken).ConfigureAwait(false);
            }

            if (progress.NextDate == date)
            {
                progress.NextDate = date == batch.To ? null : date.AddDays(1);
                progress.LastVisitedSequence = await AdvanceVisitSequenceAsync(db, cancellationToken)
                    .ConfigureAwait(false);
                progress.LastErrorCode = null;
                if (progress.NextDate is null) batch.ExpansionCompletedAt ??= now;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return receipt;
        }
        finally { _gate.Release(); }
    }

    private async Task<BackfillBatchRecovery?> ReadRecoveryMetadataAsync(CollectionPlatformDbContext db,
        BackfillBatchEntity batch, CancellationToken cancellationToken)
    {
        var progress = await db.BatchRecoveryProgress.AsNoTracking().SingleOrDefaultAsync(
            x => x.BatchId == batch.BatchId, cancellationToken).ConfigureAwait(false);
        if (progress is null)
            return new(CollectionBatchKind.Unknown, CollectionBatchRecoveryState.Unclassified, null, null);
        var state = progress.Kind == CollectionBatchKind.Unknown
            ? CollectionBatchRecoveryState.NeedsReview
            : progress.NextDate is null ? CollectionBatchRecoveryState.Completed
            : CollectionBatchRecoveryState.Ready;
        return new(progress.Kind, state, progress.NextDate, progress.ReviewReason);
    }

    private async Task<CollectionBatchCycleResult> ScanLegacyBatchesAsync(DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var hintDb = CreateDbContext())
        {
            var hasLegacy = await hintDb.BackfillBatches.AsNoTracking().AnyAsync(x =>
                x.ExpansionCompletedAt == null
                && !hintDb.BatchRecoveryProgress.Any(progress => progress.BatchId == x.BatchId),
                cancellationToken).ConfigureAwait(false);
            if (!hasLegacy) return new(0, 0, 0, 0, 0);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            db.Database.UseTransaction(transaction);
            if (await db.Controls.AsNoTracking().AnyAsync(x => x.ControlId == "pipeline" && x.IsPaused,
                    cancellationToken).ConfigureAwait(false))
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new(0, 0, 0, 0, 0, CollectionBatchCycleStopReason.Paused);
            }

            var scan = await db.BatchRecoveryScans.SingleOrDefaultAsync(x => x.ScanId == 1, cancellationToken)
                .ConfigureAwait(false);
            var lastBatchId = scan?.LastBatchId;
            var query = db.BackfillBatches.AsNoTracking()
                .Where(x => x.ExpansionCompletedAt == null
                    && !db.BatchRecoveryProgress.Any(p => p.BatchId == x.BatchId));
            var afterCursor = lastBatchId is null ? query
                : query.Where(x => string.Compare(x.BatchId, lastBatchId) > 0);
            var ids = await afterCursor.OrderBy(x => x.BatchId).Select(x => x.BatchId)
                .Take(LegacyBatchScanLimit).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (ids.Count < LegacyBatchScanLimit && lastBatchId is not null)
            {
                var wrapped = await query.Where(x => string.Compare(x.BatchId, lastBatchId) <= 0)
                    .OrderBy(x => x.BatchId).Select(x => x.BatchId)
                    .Take(LegacyBatchScanLimit - ids.Count).ToListAsync(cancellationToken).ConfigureAwait(false);
                ids.AddRange(wrapped);
            }

            if (ids.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new(0, 0, 0, 0, 0);
            }

            scan ??= new CollectionBatchRecoveryScanEntity { ScanId = 1, VisitSequence = 0 };
            if (db.Entry(scan).State == EntityState.Detached) db.BatchRecoveryScans.Add(scan);

            var visited = 0;
            foreach (var id in ids.Distinct(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = await db.BackfillBatches.SingleAsync(x => x.BatchId == id, cancellationToken)
                    .ConfigureAwait(false);
                var classification = await ClassifyBatchAsync(db, batch, cancellationToken).ConfigureAwait(false);
                db.BatchRecoveryProgress.Add(CreateProgress(batch, classification.Kind,
                    classification.ReviewReason, scan.VisitSequence++));
                scan.LastBatchId = id;
                visited++;
            }
            if (ids.Count < LegacyBatchScanLimit) scan.LastBatchId = null;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(visited, 0, 0, 0, 0);
        }
        finally { _gate.Release(); }
    }

    private async Task<CollectionBatchCycleResult> ProcessRecoveryBatchAsync(string batchId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        DateOnly expectedDate;
        long expectedSequence;
        try
        {
            await using var db = CreateDbContext();
            var progress = await db.BatchRecoveryProgress.SingleAsync(x => x.BatchId == batchId,
                cancellationToken).ConfigureAwait(false);
            var batch = await db.BackfillBatches.SingleAsync(x => x.BatchId == batchId,
                cancellationToken).ConfigureAwait(false);
            if (progress.Kind == CollectionBatchKind.Unknown || progress.NextDate is null
                || batch.ExpansionCompletedAt is not null)
                return new(0, 0, 0, 0, 0);
            expectedDate = progress.NextDate.Value;
            expectedSequence = progress.LastVisitedSequence;
        }
        finally { _gate.Release(); }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var gateHeld = true;
        try
        {
            await using var db = CreateDbContext();
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            db.Database.UseTransaction(transaction);
            var batch = await db.BackfillBatches.SingleAsync(x => x.BatchId == batchId, cancellationToken)
                .ConfigureAwait(false);
            var progress = await db.BatchRecoveryProgress.SingleAsync(x => x.BatchId == batchId,
                cancellationToken).ConfigureAwait(false);
            if (progress.NextDate != expectedDate || progress.LastVisitedSequence != expectedSequence
                || batch.ExpansionCompletedAt is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new(0, 0, 0, 0, 0);
            }

            if (await db.Controls.AsNoTracking().AnyAsync(x => x.ControlId == "pipeline" && x.IsPaused,
                    cancellationToken).ConfigureAwait(false))
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new(0, 0, 0, 0, 0, CollectionBatchCycleStopReason.Paused);
            }

            var plan = CollectionBatchRecoveryPolicy.PlanChunk(progress.Kind, batch.From, batch.To,
                expectedDate);
            var created = 0;
            var reused = 0;
            var visitedDates = 0;
            var existingBackfillDates = new HashSet<string>(StringComparer.Ordinal);
            if (progress.Kind == CollectionBatchKind.Backfill)
            {
                var resourceIds = plan.Dates.Select(date => $"backfill:{date:yyyyMMdd}").ToArray();
                var existing = await (from state in db.States.AsNoTracking()
                                      join resource in db.Resources.AsNoTracking()
                                          on state.ResourcePk equals resource.ResourcePk
                                      where state.DefinitionId == "race-discovery"
                                          && resource.Type == CollectionResourceType.Race
                                          && resource.Provider == batch.Provider
                                          && resourceIds.Contains(resource.ResourceId)
                                      select resource.ResourceId).ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                existingBackfillDates.UnionWith(existing);
            }
            foreach (var date in plan.Dates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (progress.Kind == CollectionBatchKind.Backfill
                    && existingBackfillDates.Contains($"backfill:{date:yyyyMMdd}"))
                {
                    visitedDates++;
                    continue;
                }

                var prefix = progress.Kind == CollectionBatchKind.Backfill ? "backfill:" : "recollection:";
                var receipt = await RequestCoreAsync(db,
                    new(CollectionResourceType.Race, batch.Provider, $"{prefix}{date:yyyyMMdd}"),
                    new("race-discovery"), 1,
                    progress.Kind == CollectionBatchKind.Backfill ? CollectionReason.Backfill : CollectionReason.PeriodRecollection,
                    now, CollectionLane.Background, (int)CollectionPriority.Background, null, batch.BatchId,
                    date, new Dictionary<string, string>
                    {
                        ["batchId"] = batch.BatchId,
                        ["backfillDate"] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                    }, null, cancellationToken).ConfigureAwait(false);
                if (receipt.CreatedTask) created++; else reused++;
                visitedDates++;
            }

            progress.NextDate = plan.NextDate;
            progress.LastVisitedSequence = await AdvanceVisitSequenceAsync(db, cancellationToken)
                .ConfigureAwait(false);
            progress.LastErrorCode = null;
            if (plan.Completes) batch.ExpansionCompletedAt ??= now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(0, 1, visitedDates, created, reused);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _gate.Release();
            gateHeld = false;
            await RecordBatchErrorAsync(batchId, expectedDate, expectedSequence, ex, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
        finally { if (gateHeld) _gate.Release(); }
    }

    private async Task<long> AdvanceVisitSequenceAsync(CollectionPlatformDbContext db,
        CancellationToken cancellationToken)
    {
        var scan = await GetOrCreateScanAsync(db, cancellationToken).ConfigureAwait(false);
        scan.VisitSequence++;
        return scan.VisitSequence;
    }

    private async Task RecordBatchErrorAsync(string batchId, DateOnly expectedDate, long expectedSequence,
        Exception exception, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            db.Database.UseTransaction(transaction);
            var progress = await db.BatchRecoveryProgress.SingleOrDefaultAsync(x => x.BatchId == batchId,
                cancellationToken).ConfigureAwait(false);
            if (progress is not null && progress.NextDate == expectedDate
                && progress.LastVisitedSequence == expectedSequence)
            {
                progress.LastErrorCode = exception is SqliteException ? "DatabaseUnavailable" : "RequestFailed";
                progress.LastVisitedSequence = await AdvanceVisitSequenceAsync(db, cancellationToken)
                    .ConfigureAwait(false);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<CollectionBatchCycleResult> RunBackfillRecoveryCycleAsync(DateTimeOffset now,
        CancellationToken cancellationToken = default,
        Action<CollectionBatchCycleResult>? onChunkCommitted = null,
        Func<CancellationToken, Task<CollectionBatchCycleStopReason>>? shouldContinue = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (shouldContinue is not null)
        {
            var initialStop = await shouldContinue(cancellationToken).ConfigureAwait(false);
            if (initialStop != CollectionBatchCycleStopReason.Continue)
                return new(0, 0, 0, 0, 0, initialStop);
        }
        var scan = await ScanLegacyBatchesAsync(now, cancellationToken).ConfigureAwait(false);
        var result = scan;
        if (scan.StopReason != CollectionBatchCycleStopReason.Continue) return scan;
        if (scan.LegacyRowsInspected > 0) NotifyCommitted(result);
        await using var selectionDb = CreateDbContext();
        var selected = await (from progress in selectionDb.BatchRecoveryProgress.AsNoTracking()
                              join batch in selectionDb.BackfillBatches.AsNoTracking()
                                  on progress.BatchId equals batch.BatchId
                              where progress.Kind != CollectionBatchKind.Unknown && progress.NextDate != null
                                  && batch.ExpansionCompletedAt == null
                              orderby progress.LastVisitedSequence, progress.BatchId
                              select progress.BatchId).Take(RecoveryBatchLimit).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var batchId in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shouldContinue is not null)
            {
                var stopReason = await shouldContinue(cancellationToken).ConfigureAwait(false);
                if (stopReason != CollectionBatchCycleStopReason.Continue)
                    return result with { StopReason = stopReason };
            }
            var cycle = await ProcessRecoveryBatchAsync(batchId, now, cancellationToken).ConfigureAwait(false);
            result += cycle;
            if (cycle.StopReason != CollectionBatchCycleStopReason.Continue)
                return result;
            if (cycle.BatchesVisited > 0) NotifyCommitted(result);
        }
        return result;

        void NotifyCommitted(CollectionBatchCycleResult committed)
        {
            if (onChunkCommitted is null) return;
            try { onChunkCommitted(committed); }
            catch { /* Observability must not turn a committed chunk into a retry signal. */ }
        }
    }

    public sealed record CollectionBatchCycleResult(int LegacyRowsInspected, int BatchesVisited,
        int DatesVisited, int TasksCreated, int TasksReused,
        CollectionBatchCycleStopReason StopReason = CollectionBatchCycleStopReason.Continue)
    {
        public static CollectionBatchCycleResult operator +(CollectionBatchCycleResult left,
            CollectionBatchCycleResult right) => new(left.LegacyRowsInspected + right.LegacyRowsInspected,
                left.BatchesVisited + right.BatchesVisited, left.DatesVisited + right.DatesVisited,
                left.TasksCreated + right.TasksCreated, left.TasksReused + right.TasksReused,
                right.StopReason == CollectionBatchCycleStopReason.Continue ? left.StopReason : right.StopReason);
    }
}
