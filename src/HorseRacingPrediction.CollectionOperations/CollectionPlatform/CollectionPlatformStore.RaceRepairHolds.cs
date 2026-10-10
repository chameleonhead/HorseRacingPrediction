using HorseRacingPrediction.ApiClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed record RaceRepairHoldSnapshot(string RaceId, string OperationId, long Generation,
    string Reason, DateTimeOffset CreatedAt, DateTimeOffset? ReleasedAt, string? AssignmentFingerprint,
    int ReadyTasks, int RunningTasks, IReadOnlyList<string> Blockers, int RequiredRevision,
    int UnresolvedLeases, IReadOnlyList<string> Aliases, IReadOnlyList<string> Definitions)
{
    public bool IsActive => ReleasedAt is null;
    public bool IsQuiescent => IsActive && RunningTasks == 0 && UnresolvedLeases == 0 && Blockers.Count == 0;
}

public sealed partial class CollectionPlatformStore
{
    private const int HeldResourceSnapshotPageSize = 128;
    private const string HeldResourceSnapshotTableName = "HeldResourceSnapshot";
    private const string HeldResourceWorklistTableName = "HeldResourceWorklist";

    public async Task BackupForRaceRepairAsync(string destinationPath, CancellationToken token)
    {
        await using var db = CreateDbContext();
        await db.Database.OpenConnectionAsync(token);
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = destinationPath, Pooling = false }.ToString());
        await destination.OpenAsync(token);
        ((SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(destination);
    }

    private static bool IsRaceResource(CollectionResourceEntity resource) => (resource.Type is
        CollectionResourceType.Race or CollectionResourceType.RaceCard or CollectionResourceType.RaceResult or CollectionResourceType.RaceOdds)
        && !System.Text.RegularExpressions.Regex.IsMatch(resource.ResourceId, "^((backfill|recollection):[0-9]{8}|discovery:[0-9]{10})$");

    private string? CanonicalRace(CollectionResourceEntity resource)
    {
        if (!IsRaceResource(resource)) return null;
        if (_raceIdentityResolver is not null)
        {
            try
            {
                return _raceIdentityResolver.Resolve(resource.ResourceId,
                JsonSerializer.Deserialize<Dictionary<string, string>>(resource.AttributesJson) ?? []);
            }
            catch (JsonException) { return null; }
        }
        var normalized = DeterministicIdGenerator.TryBuildRaceIdFromResource(resource.ResourceId);
        string? explicitId;
        try { explicitId = JsonSerializer.Deserialize<Dictionary<string, string>>(resource.AttributesJson)?.GetValueOrDefault("domainRaceId"); }
        catch (JsonException) { return null; }
        if (!string.IsNullOrEmpty(explicitId) && (!explicitId.StartsWith("race-", StringComparison.Ordinal)
            || !Guid.TryParseExact(explicitId[5..], "D", out _))) return null;
        var direct = System.Text.RegularExpressions.Regex.IsMatch(resource.ResourceId,
            "^race-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$") ? resource.ResourceId : null;
        var ids = new[] { normalized, explicitId, direct }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        return ids.Length == 1 ? ids[0] : null;
    }

    private bool MatchesHold(CollectionResourceEntity resource, IReadOnlyCollection<string> heldRaceIds)
        => IsRaceResource(resource) && (CanonicalRace(resource) is not { } raceId
            ? heldRaceIds.Count > 0 : heldRaceIds.Contains(raceId));

    private static async Task<string[]> ActiveHoldIdsAsync(CollectionPlatformDbContext db, CancellationToken token)
        => await db.RaceRepairHolds.Where(x => x.ReleasedAt == null).Select(x => x.RaceId).ToArrayAsync(token);

    private static IQueryable<long> HeldResourcePks(CollectionPlatformDbContext db)
        => db.Database.SqlQueryRaw<long>(
            "SELECT ResourcePk AS Value FROM temp.HeldResourceSnapshot WHERE IsHeld <> 0");

    private static async Task CreateHeldResourceSnapshotTableAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken token)
    {
        await using var create = connection.CreateCommand();
        create.Transaction = transaction;
        create.CommandText = "CREATE TEMP TABLE HeldResourceSnapshot (ResourcePk INTEGER PRIMARY KEY, IsHeld INTEGER NOT NULL);"
            + " CREATE TEMP TABLE HeldResourceWorklist (ResourcePk INTEGER PRIMARY KEY);";
        await create.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private async Task PopulateHeldResourceSnapshotAsync(CollectionPlatformDbContext db,
        SqliteConnection connection, SqliteTransaction transaction, IQueryable<long> relevantResourcePks,
        IReadOnlyCollection<string> heldRaceIds, CancellationToken token)
    {
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = $"DELETE FROM temp.{HeldResourceWorklistTableName};";
            await clear.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        var snapshotResourcePks = db.Database.SqlQueryRaw<long>(
            "SELECT ResourcePk AS Value FROM temp.HeldResourceSnapshot");
        var worklistSource = relevantResourcePks
            .Where(resourcePk => !snapshotResourcePks.Contains(resourcePk))
            .Distinct();
        await using (var seed = worklistSource.CreateDbCommand())
        {
            seed.Transaction = transaction;
            seed.CommandText = $"INSERT OR IGNORE INTO temp.{HeldResourceWorklistTableName} (ResourcePk) "
                + seed.CommandText;
            await seed.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        var scannedThrough = 0L;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var pageResourcePks = new List<long>(HeldResourceSnapshotPageSize);
            await using (var pageCommand = connection.CreateCommand())
            {
                pageCommand.Transaction = transaction;
                pageCommand.CommandText = $"SELECT ResourcePk FROM temp.{HeldResourceWorklistTableName} "
                    + "WHERE ResourcePk > $cursor ORDER BY ResourcePk LIMIT $limit;";
                pageCommand.Parameters.AddWithValue("$cursor", scannedThrough);
                pageCommand.Parameters.AddWithValue("$limit", HeldResourceSnapshotPageSize);
                await using var reader = await pageCommand.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    pageResourcePks.Add(reader.GetInt64(0));
            }
            if (pageResourcePks.Count == 0) break;

            // Resource rows are fully materialized here, so the reader is closed before identity
            // resolution can open a domain-context connection.
            var page = await db.Resources.AsNoTracking()
                .Where(resource => pageResourcePks.Contains(resource.ResourcePk))
                .OrderBy(resource => resource.ResourcePk)
                .ToListAsync(token).ConfigureAwait(false);
            if (page.Count != pageResourcePks.Count)
                throw new InvalidOperationException("A held-resource worklist page did not resolve to its complete resource set.");

            scannedThrough = page[^1].ResourcePk;
            var matches = page.Select(resource => (resource.ResourcePk, IsHeld: MatchesHold(resource, heldRaceIds)))
                .ToArray();
            token.ThrowIfCancellationRequested();
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            var values = new string[matches.Length];
            for (var index = 0; index < matches.Length; index++)
            {
                var resourceName = $"$resource{index}";
                var heldName = $"$held{index}";
                values[index] = $"({resourceName}, {heldName})";
                insert.Parameters.AddWithValue(resourceName, matches[index].ResourcePk);
                insert.Parameters.AddWithValue(heldName, matches[index].IsHeld ? 1 : 0);
            }
            insert.CommandText = $"INSERT INTO temp.{HeldResourceSnapshotTableName} (ResourcePk, IsHeld) VALUES {string.Join(",", values)};";
            await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

    }

    private async Task<bool> HasRepairHoldForResourcesAsync(CollectionPlatformDbContext db,
        SqliteConnection connection, SqliteTransaction transaction, IReadOnlyCollection<long> resourcePks,
        IReadOnlyCollection<string> heldRaceIds, CancellationToken token)
    {
        if (resourcePks.Count == 0 || heldRaceIds.Count == 0) return false;
        Exception? operationException = null;
        var snapshotCreated = false;
        try
        {
            snapshotCreated = true;
            await CreateHeldResourceSnapshotTableAsync(connection, transaction, token).ConfigureAwait(false);
            await PopulateHeldResourceSnapshotAsync(db, connection, transaction,
                db.Resources.Where(resource => resourcePks.Contains(resource.ResourcePk))
                    .Select(resource => resource.ResourcePk), heldRaceIds, token).ConfigureAwait(false);
            return await HeldResourcePks(db).AnyAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            operationException = ex;
            throw;
        }
        finally
        {
            if (snapshotCreated)
                await DropHeldResourceSnapshotAsync(connection, operationException, transaction).ConfigureAwait(false);
        }
    }

    private static async Task DropHeldResourceSnapshotAsync(SqliteConnection connection,
        Exception? operationException, SqliteTransaction? transaction = null)
    {
        try
        {
            await using var drop = connection.CreateCommand();
            drop.Transaction = transaction;
            drop.CommandText = $"DROP TABLE IF EXISTS temp.{HeldResourceWorklistTableName};"
                + $" DROP TABLE IF EXISTS temp.{HeldResourceSnapshotTableName};";
            await drop.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception cleanupException)
        {
            try { SqliteConnection.ClearPool(connection); }
            catch (Exception poolException)
            {
                System.Diagnostics.Trace.TraceError("Could not clear a SQLite pool after held-resource TEMP cleanup failed: {0}", poolException);
            }
            try { connection.Close(); }
            catch (Exception closeException)
            {
                System.Diagnostics.Trace.TraceError("Could not close a SQLite connection after held-resource TEMP cleanup failed: {0}", closeException);
            }

            System.Diagnostics.Trace.TraceError("Held-resource TEMP cleanup failed; the SQLite connection was discarded: {0}", cleanupException);
            if (operationException is null)
                throw new InvalidOperationException("Could not clean up the held-resource TEMP snapshot; the SQLite connection was discarded.", cleanupException);
            throw new AggregateException("The operation and held-resource TEMP cleanup both failed; the SQLite connection was discarded.",
                operationException, cleanupException);
        }
    }

    private async Task<bool> IsRepairHeldAsync(CollectionPlatformDbContext db, long resourcePk, CancellationToken token)
    {
        var resource = await db.Resources.SingleAsync(x => x.ResourcePk == resourcePk, token);
        return MatchesHold(resource, await ActiveHoldIdsAsync(db, token));
    }

    private async Task<HashSet<Guid>> HeldTaskIdsAsync(CollectionPlatformDbContext db, CancellationToken token, string? raceId = null)
    {
        var holds = raceId is null ? await ActiveHoldIdsAsync(db, token) : new[] { raceId };
        if (holds.Length == 0) return [];
        var resources = await db.Resources.AsNoTracking().ToListAsync(token);
        var resourceIds = resources.Where(x => MatchesHold(x, holds)).Select(x => x.ResourcePk).ToArray();
        return (await db.Tasks.Where(x => resourceIds.Contains(x.ResourcePk)).Select(x => x.TaskId).ToListAsync(token)).ToHashSet();
    }

    public async Task<RaceRepairHoldSnapshot?> GetRaceRepairHoldAsync(string raceId, CancellationToken token = default)
    {
        await using var db = CreateDbContext();
        return await HoldSnapshotAsync(db, raceId, token);
    }

    private async Task<RaceRepairHoldSnapshot?> HoldSnapshotAsync(CollectionPlatformDbContext db, string raceId, CancellationToken token,
        DateTimeOffset? observedAt = null)
    {
        var hold = await db.RaceRepairHolds.Where(x => x.RaceId == raceId).OrderByDescending(x => x.Generation).FirstOrDefaultAsync(token);
        if (hold is null) return null;
        var resources = (await db.Resources.ToListAsync(token)).Where(x =>
            IsRaceResource(x) && (CanonicalRace(x) == raceId || CanonicalRace(x) is null)).ToArray();
        var pks = resources.Select(x => x.ResourcePk).ToArray();
        var now = observedAt ?? DateTimeOffset.UtcNow;
        var tasks = await db.Tasks.Where(x => pks.Contains(x.ResourcePk)).ToListAsync(token);
        var taskIds = tasks.Select(x => x.TaskId).ToArray();
        var envelopes = await db.DispatchOutbox.Where(x => taskIds.Contains(x.TaskId) && x.EnvelopeId != null)
            .Select(x => x.EnvelopeId!.Value).Distinct().ToArrayAsync(token);
        var executions = await db.ExecutionLeases.CountAsync(x => envelopes.Contains(x.DispatchEnvelopeId)
            && (x.Status == "StartPending" || x.Status == "Running") && x.LeaseExpiresAt > now, token);
        var leases = tasks.Count(x => x.LeaseToken != null && x.LeaseExpiresAt > now);
        var active = await (from a in db.ActiveTasks
                            join t in db.Tasks on a.TaskId equals t.TaskId
                            where pks.Contains(a.ResourcePk)
                            select t).ToListAsync(token);
        var blockers = resources.Where(x => CanonicalRace(x) is null).Select(x => "UnknownRaceAlias:" + x.ResourceId).ToList();
        if (active.Any(x => x.Status is not (CollectionTaskStatus.Ready or CollectionTaskStatus.Running))) blockers.Add("UnexpectedActiveTaskState");
        var definitions = await db.States.Where(x => pks.Contains(x.ResourcePk)).Select(x => x.DefinitionId).Distinct().ToArrayAsync(token);
        blockers.AddRange(definitions.Where(x => x is not ("race-detail" or "race-odds"))
            .Select(x => "UnknownRaceDefinition:" + x));
        var required = await db.States.Where(x => pks.Contains(x.ResourcePk) && x.DefinitionId == "race-detail")
            .Select(x => (int?)x.RequiredRevision).MaxAsync(token) ?? 0;
        return new(raceId, hold.OperationId, hold.Generation, hold.Reason, hold.CreatedAt, hold.ReleasedAt,
            hold.AssignmentFingerprint, active.Count(x => x.Status == CollectionTaskStatus.Ready),
            tasks.Count(x => x.Status == CollectionTaskStatus.Running), blockers, required, executions + leases,
            resources.Select(x => x.ResourceId).ToArray(), definitions);
    }

    // Callers that also touch domain data must hold the RaceWriteCoordinator lock first.
    public async Task<RaceRepairHoldSnapshot> HoldRaceForRepairAsync(string raceId, string operationId,
        long expectedGeneration, string reason, DateTimeOffset now, CancellationToken token = default, string? assignmentFingerprint = null)
    {
        if (!Guid.TryParse(operationId, out _) || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000
            || !raceId.StartsWith("race-", StringComparison.Ordinal) || !Guid.TryParseExact(raceId[5..], "D", out _))
            throw new ArgumentException("A race, operation id and bounded reason are required.");
        await _gate.WaitAsync(token);
        try
        {
            await using var db = CreateDbContext();
            await db.Database.OpenConnectionAsync(token);
            await using var tx = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
            db.Database.UseTransaction(tx);
            var previous = await db.RaceRepairHolds.Where(x => x.RaceId == raceId).OrderByDescending(x => x.Generation).FirstOrDefaultAsync(token);
            if (previous?.OperationId == operationId)
            {
                if (previous.Reason != reason) throw new InvalidOperationException("HoldOperationMismatch");
                if (previous.ReleasedAt is null) await ReclaimExpiredAsync(db, now, token, await HeldTaskIdsAsync(db, token, raceId));
                await db.SaveChangesAsync(token);
                var existing = (await HoldSnapshotAsync(db, raceId, token, now))!;
                await tx.CommitAsync(token);
                return existing;
            }
            if (previous is { ReleasedAt: null } || (previous?.Generation ?? 0) != expectedGeneration)
                throw new InvalidOperationException("StaleRepairHold");
            db.RaceRepairHolds.Add(new()
            {
                RaceId = raceId,
                OperationId = operationId,
                Generation = expectedGeneration + 1,
                Reason = reason,
                CreatedAt = now,
                AssignmentFingerprint = assignmentFingerprint
            });
            await db.SaveChangesAsync(token);
            // Invalidate reservations, not requests or attempts. Mixed envelopes retain their unheld rows.
            var heldIds = await HeldTaskIdsAsync(db, token, raceId);
            var outboxes = await db.DispatchOutbox.Where(x => heldIds.Contains(x.TaskId)).ToListAsync(token);
            foreach (var row in outboxes)
            {
                row.ReservationToken = null; row.ReservedUntilUnixMilliseconds = null; row.WakeId = null;
                row.DispatchedAt = now;
            }
            await ReclaimExpiredAsync(db, now, token, heldIds);
            await db.SaveChangesAsync(token);
            var result = (await HoldSnapshotAsync(db, raceId, token, now))!;
            await tx.CommitAsync(token);
            return result;
        }
        finally { _gate.Release(); }
    }

    private static async Task<CollectionRequestReceipt> DeferRequestAsync(CollectionPlatformDbContext db,
        CollectionRequestEntity request, DateTimeOffset now, CancellationToken token)
    {
        if (db.Entry(request).State == EntityState.Detached)
        {
            if (await db.Requests.AnyAsync(x => x.RequestId == request.RequestId, token)) db.Requests.Attach(request);
            else db.Requests.Add(request);
        }
        var state = await db.States.SingleOrDefaultAsync(x => x.ResourcePk == request.ResourcePk && x.DefinitionId == request.DefinitionId, token);
        if (state is null)
        {
            state = new() { ResourcePk = request.ResourcePk, DefinitionId = request.DefinitionId, Status = CollectionStateStatus.Pending };
            db.States.Add(state);
        }
        state.RequiredRevision = Math.Max(state.RequiredRevision, request.RequestedRevision);
        state.UpdatedAt = now;
        await db.SaveChangesAsync(token);
        var active = await db.ActiveTasks.SingleOrDefaultAsync(x => x.ResourcePk == request.ResourcePk && x.DefinitionId == request.DefinitionId, token);
        return new(request.RequestId, active?.TaskId, false, true);
    }

    private async Task<CollectionRequestReceipt> ExistingReceiptAsync(CollectionPlatformDbContext db,
        Guid requestId, Guid? taskId, CancellationToken token)
    {
        var request = await db.Requests.SingleAsync(x => x.RequestId == requestId, token);
        var held = await IsRepairHeldAsync(db, request.ResourcePk, token);
        var active = await db.ActiveTasks.SingleOrDefaultAsync(x => x.ResourcePk == request.ResourcePk && x.DefinitionId == request.DefinitionId, token);
        return new(requestId, active?.TaskId ?? taskId, false, held);
    }

    public async Task<RaceRepairHoldSnapshot> ReleaseRaceRepairHoldAsync(string raceId, string holdOperationId,
        long generation, string releaseOperationId, string assignmentFingerprint, DateTimeOffset now, CancellationToken token = default)
    {
        if (!Guid.TryParse(releaseOperationId, out _) || string.IsNullOrWhiteSpace(assignmentFingerprint))
            throw new ArgumentException("Release operation and verified assignment are required.");
        await _gate.WaitAsync(token);
        try
        {
            await using var db = CreateDbContext();
            await db.Database.OpenConnectionAsync(token);
            await using var tx = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
            db.Database.UseTransaction(tx);
            var snapshot = await HoldSnapshotAsync(db, raceId, token, now) ?? throw new InvalidOperationException("RepairHoldRequired");
            var hold = await db.RaceRepairHolds.SingleAsync(x => x.RaceId == raceId && x.Generation == generation, token);
            if (snapshot.Generation != generation || hold.OperationId != holdOperationId) throw new InvalidOperationException("StaleRepairHold");
            if (hold.ReleasedAt is not null)
            {
                if (hold.ReleaseOperationId != releaseOperationId || hold.AssignmentFingerprint != assignmentFingerprint)
                    throw new InvalidOperationException("ReleaseOperationMismatch");
                return snapshot;
            }
            if (!snapshot.IsQuiescent) throw new InvalidOperationException("RepairHoldNotQuiescent");
            var resources = (await db.Resources.ToListAsync(token)).Where(x => CanonicalRace(x) == raceId).ToArray();
            if (!snapshot.Definitions.Contains("race-detail")) throw new InvalidOperationException("RaceDetailRequestRequired");
            hold.ReleasedAt = now; hold.AssignmentFingerprint = assignmentFingerprint; hold.ReleaseOperationId = releaseOperationId;
            await db.SaveChangesAsync(token);
            foreach (var resource in resources)
            {
                var states = await db.States.Where(x => x.ResourcePk == resource.ResourcePk).ToListAsync(token);
                foreach (var state in states)
                {
                    var definition = await db.Definitions.SingleAsync(x => x.DefinitionId == state.DefinitionId, token);
                    if (!definition.Enabled) throw new InvalidOperationException("RepairReleaseDefinitionDisabled");
                    var active = await db.ActiveTasks.SingleOrDefaultAsync(x => x.ResourcePk == resource.ResourcePk && x.DefinitionId == state.DefinitionId, token);
                    if (active is not null)
                    {
                        var old = await db.Tasks.SingleAsync(x => x.TaskId == active.TaskId, token);
                        if (old.Status != CollectionTaskStatus.Ready) throw new InvalidOperationException("ActiveCollection");
                        old.Status = CollectionTaskStatus.Cancelled; old.FinishedAt = now; old.UpdatedAt = now;
                        db.ActiveTasks.Remove(active);
                    }
                    await db.SaveChangesAsync(token);
                    var revision = Math.Max(state.RequiredRevision, definition.CurrentRevision);
                    if (state.DefinitionId == "race-detail") revision = Math.Max(4, revision);
                    var intents = await db.Requests.Where(x => x.ResourcePk == resource.ResourcePk && x.DefinitionId == state.DefinitionId)
                        .OrderByDescending(x => x.RequestedAt).ToListAsync(token);
                    var intent = intents.FirstOrDefault();
                    var pendingIntents = intents.Where(x => x.RequestedAt >= hold.CreatedAt).ToArray();
                    var lane = pendingIntents.Aggregate(intent?.Lane ?? CollectionLane.Normal, (current, x) => StrongerLane(current, x.Lane));
                    var priority = pendingIntents.Select(x => x.Priority).Append(intent?.Priority ?? (int)CollectionPriority.Normal).Max();
                    if (active is not null)
                    {
                        var replaced = await db.Tasks.SingleAsync(x => x.TaskId == active.TaskId, token);
                        lane = StrongerLane(lane, replaced.Lane);
                        priority = Math.Max(priority, replaced.Priority);
                    }
                    var receipt = await RequestCoreAsync(db, new(resource.Type, resource.Provider, resource.ResourceId),
                        new(state.DefinitionId), revision, CollectionReason.Recovery, now, lane,
                        priority,
                        Uri.TryCreate(intent?.ExplicitUrl, UriKind.Absolute, out var requestedUrl) ? requestedUrl : null,
                        "repair-release:" + releaseOperationId + ":" + resource.ResourcePk + ":" + state.DefinitionId,
                        resource.EffectiveDate, DeserializeTaskMetadata(intent?.MetadataJson ?? resource.AttributesJson), null, token);
                    if (receipt.TaskId is null || !receipt.CreatedTask) throw new InvalidOperationException("RepairReleaseNotMaterialized");
                    var intentIds = intents.Select(x => x.RequestId).ToArray();
                    foreach (var binding in await db.RequestBatchBindings.Where(x => intentIds.Contains(x.RequestId) && x.TaskId == null).ToListAsync(token))
                        binding.TaskId = receipt.TaskId;
                    var oldIds = await db.Tasks.Where(x => x.ResourcePk == resource.ResourcePk && x.DefinitionId == state.DefinitionId)
                        .Select(x => x.TaskId).ToArrayAsync(token);
                    foreach (var failure in await db.FailureNotifications.Where(x => oldIds.Contains(x.TaskId)
                        && x.ResolutionStatus == CollectionFailureResolutionStatus.Open).ToListAsync(token))
                    { failure.RecoveryTaskId = receipt.TaskId; failure.RecoveryStartedAt = now; }
                    await db.SaveChangesAsync(token);
                }
            }
            var result = (await HoldSnapshotAsync(db, raceId, token, now))!;
            await tx.CommitAsync(token);
            return result;
        }
        finally { _gate.Release(); }
    }
}
