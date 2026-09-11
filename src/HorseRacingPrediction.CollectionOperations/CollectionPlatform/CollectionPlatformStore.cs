using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed class CollectionPlatformStore
{
    private readonly DbContextOptions<CollectionPlatformDbContext> _dbOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CollectionPlatformStore(IOptions<CollectionPlatformOptions> options)
    {
        var value = options.Value;
        var directory = Path.GetFullPath(string.IsNullOrWhiteSpace(value.StateDirectory)
            ? "collection-platform-state" : value.StateDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, string.IsNullOrWhiteSpace(value.DatabaseFileName)
            ? "collection-platform.db" : value.DatabaseFileName);
        _dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options;
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    internal CollectionPlatformStore(DbContextOptions<CollectionPlatformDbContext> dbOptions)
    {
        _dbOptions = dbOptions;
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public async Task RegisterDefinitionAsync(CollectionDefinitionId id, string name, ResourceType resourceType,
        int currentRevision, string revisionDescription, bool mayRequireRecollection,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id.Value)) throw new ArgumentException("Definition id is required.", nameof(id));
        if (currentRevision < 1) throw new ArgumentOutOfRangeException(nameof(currentRevision));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var definition = await db.Definitions.SingleOrDefaultAsync(x => x.DefinitionId == id.Value, cancellationToken);
            if (definition is null)
            {
                definition = new CollectionDefinitionEntity
                {
                    DefinitionId = id.Value, Name = name, ResourceType = resourceType,
                    CurrentRevision = currentRevision, Enabled = true,
                };
                db.Definitions.Add(definition);
            }
            else
            {
                if (definition.ResourceType != resourceType)
                    throw new InvalidOperationException($"Definition {id} is already registered for {definition.ResourceType}.");
                definition.Name = name;
                definition.CurrentRevision = Math.Max(definition.CurrentRevision, currentRevision);
                definition.Enabled = true;
            }

            if (!await db.Revisions.AnyAsync(x => x.DefinitionId == id.Value && x.Revision == currentRevision, cancellationToken))
                db.Revisions.Add(new CollectionRevisionEntity
                {
                    DefinitionId = id.Value, Revision = currentRevision, Description = revisionDescription,
                    MayRequireRecollection = mayRequireRecollection, CreatedAt = DateTimeOffset.UtcNow,
                });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<CollectionRequestReceipt> RequestAsync(ResourceKey resource, CollectionDefinitionId definition,
        int requestedRevision, CollectionReason reason, DateTimeOffset requestedAt,
        CollectionLane lane = CollectionLane.Normal, int priority = (int)CollectionPriority.Normal,
        Uri? explicitUrl = null, string? batchId = null, DateOnly? effectiveDate = null,
        IReadOnlyDictionary<string, string>? attributes = null, CancellationToken cancellationToken = default)
    {
        resource = resource.Normalize();
        if (string.IsNullOrWhiteSpace(resource.Provider) || string.IsNullOrWhiteSpace(resource.Id))
            throw new ArgumentException("Provider and resource id are required.", nameof(resource));
        if (requestedRevision < 1) throw new ArgumentOutOfRangeException(nameof(requestedRevision));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var definitionEntity = await db.Definitions.SingleOrDefaultAsync(x => x.DefinitionId == definition.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Collection definition {definition} is not registered.");
            if (!definitionEntity.Enabled || definitionEntity.ResourceType != resource.Type)
                throw new InvalidOperationException($"Definition {definition} cannot collect {resource.Type}.");
            if (requestedRevision > definitionEntity.CurrentRevision
                || !await db.Revisions.AnyAsync(x => x.DefinitionId == definition.Value
                    && x.Revision == requestedRevision, cancellationToken))
                throw new InvalidOperationException(
                    $"Revision {requestedRevision} is not registered for definition {definition}.");

            var resourceEntity = await db.Resources.SingleOrDefaultAsync(x => x.Type == resource.Type
                && x.Provider == resource.Provider && x.ResourceId == resource.Id, cancellationToken);
            if (resourceEntity is null)
            {
                resourceEntity = new CollectionResourceEntity
                {
                    Type = resource.Type, Provider = resource.Provider, ResourceId = resource.Id,
                    EffectiveDate = effectiveDate, AttributesJson = JsonSerializer.Serialize(attributes ?? new Dictionary<string, string>()),
                    CreatedAt = requestedAt,
                };
                db.Resources.Add(resourceEntity);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                resourceEntity.EffectiveDate ??= effectiveDate;
                if (attributes is not null) resourceEntity.AttributesJson = JsonSerializer.Serialize(attributes);
            }

            var request = new CollectionRequestEntity
            {
                RequestId = Guid.NewGuid(), ResourcePk = resourceEntity.ResourcePk, DefinitionId = definition.Value,
                RequestedRevision = requestedRevision, Reason = reason, RequestedAt = requestedAt,
                ExplicitUrl = explicitUrl?.AbsoluteUri, BatchId = batchId,
            };
            db.Requests.Add(request);

            var active = await db.ActiveTasks.SingleOrDefaultAsync(x => x.ResourcePk == resourceEntity.ResourcePk
                && x.DefinitionId == definition.Value, cancellationToken);
            if (active is not null)
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new CollectionRequestReceipt(request.RequestId, active.TaskId, false);
            }

            var task = new CollectionTaskEntity
            {
                TaskId = Guid.NewGuid(), RequestId = request.RequestId, ResourcePk = resourceEntity.ResourcePk,
                DefinitionId = definition.Value, RequestedRevision = requestedRevision,
                Status = CollectionTaskStatus.Ready, Lane = lane, Priority = priority,
                AvailableAt = requestedAt, CreatedAt = requestedAt, UpdatedAt = requestedAt,
                DispatchGeneration = 1,
            };
            db.Tasks.Add(task);
            db.ActiveTasks.Add(new CollectionActiveTaskEntity
                { ResourcePk = resourceEntity.ResourcePk, DefinitionId = definition.Value, TaskId = task.TaskId });
            db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
            {
                OutboxId = Guid.NewGuid(), TaskId = task.TaskId, DispatchGeneration = task.DispatchGeneration,
                AvailableAt = requestedAt, CreatedAt = requestedAt,
            });
            var state = await db.States.SingleOrDefaultAsync(x => x.ResourcePk == resourceEntity.ResourcePk
                && x.DefinitionId == definition.Value, cancellationToken);
            if (state is null)
                db.States.Add(new CollectionStateEntity
                {
                    ResourcePk = resourceEntity.ResourcePk, DefinitionId = definition.Value,
                    RequiredRevision = requestedRevision, Status = CollectionStateStatus.Pending, UpdatedAt = requestedAt,
                });
            else
            {
                state.RequiredRevision = Math.Max(state.RequiredRevision, requestedRevision);
                state.Status = CollectionStateStatus.Pending;
                state.UpdatedAt = requestedAt;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new CollectionRequestReceipt(request.RequestId, task.TaskId, true);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            throw new InvalidOperationException("The resource already has an active collection task.", ex);
        }
        finally { _gate.Release(); }
    }

    public async Task<LeasedCollectionTask?> AcquireAsync(Guid taskId, long dispatchGeneration,
        DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await ReclaimExpiredAsync(db, now, cancellationToken).ConfigureAwait(false);
            var task = await db.Tasks.SingleOrDefaultAsync(x => x.TaskId == taskId, cancellationToken);
            if (task is null || task.Status != CollectionTaskStatus.Ready || task.AvailableAt > now
                || task.DispatchGeneration != dispatchGeneration) return null;
            var request = await db.Requests.SingleAsync(x => x.RequestId == task.RequestId, cancellationToken);
            var resource = await db.Resources.SingleAsync(x => x.ResourcePk == task.ResourcePk, cancellationToken);
            task.Status = CollectionTaskStatus.Running;
            task.LeaseToken = Guid.NewGuid().ToString("N");
            task.LeaseExpiresAt = now.Add(leaseDuration);
            task.StartedAt ??= now;
            task.UpdatedAt = now;
            task.AttemptCount++;
            db.Attempts.Add(new CollectionAttemptEntity
            {
                AttemptId = Guid.NewGuid(), TaskId = task.TaskId, AttemptNumber = task.AttemptCount,
                StartedAt = now, Result = CollectionAttemptResult.Running,
            });
            var state = await db.States.SingleAsync(x => x.ResourcePk == task.ResourcePk
                && x.DefinitionId == task.DefinitionId, cancellationToken);
            state.Status = CollectionStateStatus.Collecting;
            state.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new LeasedCollectionTask(task.TaskId, task.RequestId,
                new ResourceKey(resource.Type, resource.Provider, resource.ResourceId),
                new CollectionDefinitionId(task.DefinitionId), task.RequestedRevision, request.Reason,
                task.Lane, task.Priority, task.LeaseToken, task.LeaseExpiresAt.Value,
                resource.EffectiveDate,
                JsonSerializer.Deserialize<Dictionary<string, string>>(resource.AttributesJson) ?? []);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> CompleteAttemptAsync(Guid taskId, string leaseToken, DateTimeOffset now,
        CollectionAttemptCompletion completion, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var task = await db.Tasks.SingleOrDefaultAsync(x => x.TaskId == taskId, cancellationToken);
            if (task is null || task.Status != CollectionTaskStatus.Running
                || !string.Equals(task.LeaseToken, leaseToken, StringComparison.Ordinal)) return false;
            var attempt = await db.Attempts.SingleAsync(x => x.TaskId == taskId
                && x.AttemptNumber == task.AttemptCount, cancellationToken);
            attempt.Result = completion.Result;
            attempt.FinishedAt = now;
            attempt.ErrorCode = completion.ErrorCode;
            attempt.ErrorMessage = completion.ErrorMessage;
            attempt.RequestedUrl = completion.RequestedUrl?.AbsoluteUri;
            attempt.FinalUrl = completion.FinalUrl?.AbsoluteUri;
            attempt.HttpStatusCode = completion.HttpStatusCode;
            attempt.PageIdentification = completion.PageIdentification;
            task.LeaseToken = null;
            task.LeaseExpiresAt = null;
            task.UpdatedAt = now;
            var state = await db.States.SingleAsync(x => x.ResourcePk == task.ResourcePk
                && x.DefinitionId == task.DefinitionId, cancellationToken);

            if (completion.Result == CollectionAttemptResult.Succeeded)
            {
                task.Status = CollectionTaskStatus.Succeeded;
                task.FinishedAt = now;
                state.AppliedRevision = Math.Max(state.AppliedRevision, task.RequestedRevision);
                state.LastCollectedAt = now;
                state.NextCollectionAt = completion.NextCollectionAt;
                state.Status = CollectionStateStatus.Current;
                db.ActiveTasks.Remove(await db.ActiveTasks.SingleAsync(x => x.TaskId == taskId, cancellationToken));
            }
            else if (completion.Result == CollectionAttemptResult.ResourceNotYetAvailable || completion.RetryAt.HasValue)
            {
                task.Status = CollectionTaskStatus.RetryWaiting;
                task.AvailableAt = completion.RetryAt ?? now;
                task.DispatchGeneration++;
                state.NextCollectionAt = task.AvailableAt;
                state.Status = CollectionStateStatus.Pending;
                db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
                {
                    OutboxId = Guid.NewGuid(), TaskId = task.TaskId, DispatchGeneration = task.DispatchGeneration,
                    AvailableAt = task.AvailableAt, CreatedAt = now,
                });
                task.Status = CollectionTaskStatus.Ready;
            }
            else
            {
                task.Status = CollectionTaskStatus.Failed;
                task.FinishedAt = now;
                state.Status = completion.Result == CollectionAttemptResult.ResourceNotFound
                    ? CollectionStateStatus.Unavailable : CollectionStateStatus.Failed;
                db.ActiveTasks.Remove(await db.ActiveTasks.SingleAsync(x => x.TaskId == taskId, cancellationToken));
            }
            state.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<CollectionStateSnapshot?> GetStateAsync(ResourceKey resource, CollectionDefinitionId definition,
        CancellationToken cancellationToken = default)
    {
        resource = resource.Normalize();
        await using var db = CreateDbContext();
        var row = await (from state in db.States.AsNoTracking()
            join item in db.Resources.AsNoTracking() on state.ResourcePk equals item.ResourcePk
            where item.Type == resource.Type && item.Provider == resource.Provider && item.ResourceId == resource.Id
                && state.DefinitionId == definition.Value
            select new { state, item }).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new CollectionStateSnapshot(
            new ResourceKey(row.item.Type, row.item.Provider, row.item.ResourceId), definition,
            row.state.AppliedRevision, row.state.RequiredRevision, row.state.LastCollectedAt,
            row.state.NextCollectionAt, row.state.Status);
    }

    public async Task<bool> HasActiveTaskAsync(ResourceKey resource, CollectionDefinitionId definition,
        CancellationToken cancellationToken = default)
    {
        resource = resource.Normalize();
        await using var db = CreateDbContext();
        return await (from active in db.ActiveTasks.AsNoTracking()
            join item in db.Resources.AsNoTracking() on active.ResourcePk equals item.ResourcePk
            where item.Type == resource.Type && item.Provider == resource.Provider
                && item.ResourceId == resource.Id && active.DefinitionId == definition.Value
            select active).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CollectionStateSnapshot>> GetDueStatesAsync(DateTimeOffset now, int limit = 500,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var rows = await (from state in db.States.AsNoTracking()
            join item in db.Resources.AsNoTracking() on state.ResourcePk equals item.ResourcePk
            where state.Status != CollectionStateStatus.Collecting
            select new { state, item }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Where(x => x.state.NextCollectionAt is not null && x.state.NextCollectionAt <= now)
            .OrderBy(x => x.state.NextCollectionAt).Take(Math.Max(1, limit))
            .Select(x => new CollectionStateSnapshot(
                new(x.item.Type, x.item.Provider, x.item.ResourceId), new(x.state.DefinitionId),
                x.state.AppliedRevision, x.state.RequiredRevision, x.state.LastCollectedAt,
                x.state.NextCollectionAt, x.state.Status)).ToList();
    }

    public async Task<int> ReclaimExpiredLeasesAsync(DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var before = await db.Tasks.CountAsync(x => x.Status == CollectionTaskStatus.Running, cancellationToken);
            await ReclaimExpiredAsync(db, now, cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            var after = await db.Tasks.CountAsync(x => x.Status == CollectionTaskStatus.Running, cancellationToken);
            return before - after;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<CollectionAttemptEntity>> GetAttemptsAsync(Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        return await db.Attempts.AsNoTracking().Where(x => x.TaskId == taskId)
            .OrderBy(x => x.AttemptNumber).ToListAsync(cancellationToken);
    }

    public async Task<int> AddRevisionAndApplyImpactAsync(CollectionDefinitionId definition, int revision,
        string description, RevisionImpact impact, IEnumerable<INamedRevisionImpactCondition> namedConditions,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var definitionEntity = await db.Definitions.SingleAsync(x => x.DefinitionId == definition.Value, cancellationToken);
            if (await db.Revisions.AnyAsync(x => x.DefinitionId == definition.Value && x.Revision == revision, cancellationToken))
                throw new InvalidOperationException($"Revision {definition}:{revision} already exists.");
            ValidateImpact(impact, namedConditions);
            definitionEntity.CurrentRevision = Math.Max(definitionEntity.CurrentRevision, revision);
            db.Revisions.Add(new CollectionRevisionEntity
            {
                DefinitionId = definition.Value, Revision = revision, Description = description,
                MayRequireRecollection = true, CreatedAt = now,
            });
            db.RevisionImpacts.Add(new CollectionRevisionImpactEntity
            {
                DefinitionId = definition.Value, Revision = revision,
                ScopeType = impact.ScopeType, ScopePayload = impact.ScopePayload,
            });

            var candidates = await (from state in db.States
                join resource in db.Resources on state.ResourcePk equals resource.ResourcePk
                where state.DefinitionId == definition.Value
                select new { state, resource }).ToListAsync(cancellationToken);
            var conditions = namedConditions.ToDictionary(x => x.Name, StringComparer.Ordinal);
            var affected = 0;
            foreach (var item in candidates)
            {
                var attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(item.resource.AttributesJson) ?? [];
                var candidate = new RevisionResourceCandidate(
                    new ResourceKey(item.resource.Type, item.resource.Provider, item.resource.ResourceId),
                    item.resource.EffectiveDate, attributes);
                if (!MatchesImpact(candidate, impact, conditions)) continue;
                item.state.RequiredRevision = Math.Max(item.state.RequiredRevision, revision);
                if (item.state.AppliedRevision < item.state.RequiredRevision)
                    item.state.Status = CollectionStateStatus.Stale;
                item.state.UpdatedAt = now;
                affected++;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return affected;
        }
        finally { _gate.Release(); }
    }

    public async Task<long> UpsertLocationAsync(ResourceKey resource, CollectionDefinitionId definition, Uri url,
        ResourceLocationSource source, DateTimeOffset discoveredAt, CancellationToken cancellationToken = default)
    {
        resource = resource.Normalize();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var item = await db.Resources.SingleAsync(x => x.Type == resource.Type && x.Provider == resource.Provider
                && x.ResourceId == resource.Id, cancellationToken);
            var location = await db.Locations.SingleOrDefaultAsync(x => x.ResourcePk == item.ResourcePk
                && x.DefinitionId == definition.Value && x.Url == url.AbsoluteUri, cancellationToken);
            if (location is null)
            {
                location = new ResourceLocationEntity
                {
                    ResourcePk = item.ResourcePk, DefinitionId = definition.Value, Url = url.AbsoluteUri,
                    Source = source, Status = ResourceLocationStatus.Unknown, DiscoveredAt = discoveredAt,
                };
                db.Locations.Add(location);
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return location.LocationId;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<ResourceLocationCandidate>> ResolveLocationsAsync(ResourceKey resource,
        CollectionDefinitionId definition, CancellationToken cancellationToken = default)
    {
        resource = resource.Normalize();
        await using var db = CreateDbContext();
        var rows = await (from location in db.Locations.AsNoTracking()
            join item in db.Resources.AsNoTracking() on location.ResourcePk equals item.ResourcePk
            where item.Type == resource.Type && item.Provider == resource.Provider && item.ResourceId == resource.Id
                && location.DefinitionId == definition.Value && location.Status != ResourceLocationStatus.Invalid
            select location).ToListAsync(cancellationToken);
        return rows.OrderBy(x => x.Status == ResourceLocationStatus.Active ? 0 : x.Status == ResourceLocationStatus.Unknown ? 1 : 2)
            .ThenByDescending(x => x.LastVerifiedAt)
            .Select(x => new ResourceLocationCandidate(x.LocationId, new Uri(x.Url), x.Source, x.Status, x.LastVerifiedAt))
            .ToList();
    }

    public async Task RecordLocationOutcomeAsync(long locationId, CollectionAttemptResult result, DateTimeOffset now,
        string? errorCode = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var location = await db.Locations.SingleAsync(x => x.LocationId == locationId, cancellationToken);
            switch (result)
            {
                case CollectionAttemptResult.Succeeded:
                    location.Status = ResourceLocationStatus.Active;
                    location.LastVerifiedAt = now;
                    location.LastFailureCode = null;
                    break;
                case CollectionAttemptResult.ResourceNotFound:
                case CollectionAttemptResult.UnexpectedPage:
                case CollectionAttemptResult.ValidationFailure:
                    location.Status = ResourceLocationStatus.Suspect;
                    location.LastFailedAt = now;
                    location.LastFailureCode = errorCode ?? result.ToString();
                    break;
                default:
                    location.LastFailedAt = now;
                    location.LastFailureCode = errorCode ?? result.ToString();
                    break;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<PendingCollectionDispatch>> GetPendingDispatchesAsync(DateTimeOffset now, int maxCount,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var pending = await (from outbox in db.DispatchOutbox.AsNoTracking()
            join task in db.Tasks.AsNoTracking() on outbox.TaskId equals task.TaskId
            where outbox.DispatchedAt == null
            select new { outbox, task }).ToListAsync(cancellationToken);
        return pending.Where(x => x.outbox.AvailableAt <= now).OrderBy(x => x.outbox.AvailableAt)
            .Take(Math.Max(1, maxCount))
            .Select(x => new PendingCollectionDispatch(x.outbox.OutboxId,
                new CollectionTaskNotification(x.outbox.TaskId, x.outbox.DispatchGeneration),
                x.task.Lane, x.task.Priority, x.outbox.AvailableAt, x.outbox.CreatedAt)).ToList();
    }

    public async Task MarkDispatchedAsync(Guid outboxId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var row = await db.DispatchOutbox.SingleAsync(x => x.OutboxId == outboxId, cancellationToken);
            row.DispatchedAt ??= now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<CollectionTaskSummary>> GetTasksAsync(CollectionTaskStatus? status = null,
        int limit = 200, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var query = from task in db.Tasks.AsNoTracking()
            join resource in db.Resources.AsNoTracking() on task.ResourcePk equals resource.ResourcePk
            select new { task, resource };
        if (status.HasValue) query = query.Where(x => x.task.Status == status.Value);
        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.OrderByDescending(x => x.task.Priority).ThenBy(x => x.task.AvailableAt)
            .Take(Math.Clamp(limit, 1, 1000)).Select(x => new CollectionTaskSummary(x.task.TaskId,
            new ResourceKey(x.resource.Type, x.resource.Provider, x.resource.ResourceId),
            new CollectionDefinitionId(x.task.DefinitionId), x.task.Status, x.task.Lane, x.task.Priority,
            x.task.RequestedRevision, x.task.AvailableAt, x.task.AttemptCount)).ToList();
    }

    public async Task<CollectionProgressSnapshot> GetProgressAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var resources = await db.Resources.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var states = await db.States.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var tasks = await db.Tasks.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var active = tasks.Where(x => x.Status is CollectionTaskStatus.Pending or CollectionTaskStatus.Ready
            or CollectionTaskStatus.Running or CollectionTaskStatus.RetryWaiting or CollectionTaskStatus.WaitingDiscovery)
            .ToList();
        return new(
            resources.GroupBy(x => x.Type).ToDictionary(x => x.Key, x => x.Count()),
            states.GroupBy(x => x.Status).ToDictionary(x => x.Key, x => x.Count()),
            active.GroupBy(x => x.Lane).ToDictionary(x => x.Key, x => x.Count()),
            active.GroupBy(x => x.Priority).ToDictionary(x => x.Key, x => x.Count()),
            states.GroupBy(x => x.DefinitionId).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal),
            tasks.Count(x => x.Status == CollectionTaskStatus.RetryWaiting));
    }

    private static void ValidateImpact(RevisionImpact impact, IEnumerable<INamedRevisionImpactCondition> namedConditions)
    {
        if (impact.ScopeType == RevisionImpactScopeType.NamedCondition
            && !namedConditions.Any(x => string.Equals(x.Name, impact.ScopePayload, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Named revision condition '{impact.ScopePayload}' is not registered.");
        if (impact.ScopeType is RevisionImpactScopeType.SpecificResources or RevisionImpactScopeType.DateRange
            && string.IsNullOrWhiteSpace(impact.ScopePayload))
            throw new InvalidOperationException("Revision impact payload is required.");
    }

    private static bool MatchesImpact(RevisionResourceCandidate candidate, RevisionImpact impact,
        IReadOnlyDictionary<string, INamedRevisionImpactCondition> conditions)
        => impact.ScopeType switch
        {
            RevisionImpactScopeType.All => true,
            RevisionImpactScopeType.SpecificResources =>
                (JsonSerializer.Deserialize<string[]>(impact.ScopePayload) ?? [])
                    .Contains(candidate.Resource.Id, StringComparer.Ordinal),
            RevisionImpactScopeType.DateRange => MatchesDateRange(candidate.EffectiveDate, impact.ScopePayload),
            RevisionImpactScopeType.NamedCondition => conditions[impact.ScopePayload].Matches(candidate),
            _ => false,
        };

    private static bool MatchesDateRange(DateOnly? date, string payload)
    {
        if (date is null) return false;
        var range = JsonSerializer.Deserialize<DateRangeImpact>(payload)
            ?? throw new InvalidOperationException("Date range impact is invalid.");
        return date >= range.From && date <= range.To;
    }

    private sealed record DateRangeImpact(DateOnly From, DateOnly To);

    private async Task ReclaimExpiredAsync(CollectionPlatformDbContext db, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var running = await db.Tasks.Where(x => x.Status == CollectionTaskStatus.Running
            && x.LeaseExpiresAt != null).ToListAsync(cancellationToken);
        var expired = running.Where(x => x.LeaseExpiresAt <= now).ToList();
        foreach (var task in expired)
        {
            var attempt = await db.Attempts.SingleAsync(x => x.TaskId == task.TaskId
                && x.AttemptNumber == task.AttemptCount, cancellationToken);
            attempt.Result = CollectionAttemptResult.TransientFailure;
            attempt.ErrorCode = "LeaseExpired";
            attempt.FinishedAt = now;
            task.Status = CollectionTaskStatus.Ready;
            task.AvailableAt = now;
            task.LeaseToken = null;
            task.LeaseExpiresAt = null;
            task.DispatchGeneration++;
            task.UpdatedAt = now;
            db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
            {
                OutboxId = Guid.NewGuid(), TaskId = task.TaskId, DispatchGeneration = task.DispatchGeneration,
                AvailableAt = now, CreatedAt = now,
            });
        }
        if (expired.Count > 0) await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private CollectionPlatformDbContext CreateDbContext() => new(_dbOptions);
}
