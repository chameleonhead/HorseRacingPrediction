using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed partial class CollectionPlatformStore
{
    private const int MaxLocationOutcomesPerCompletion = 100;
    private static readonly HashSet<string> AllowedTaskMetadataKeys = new(StringComparer.Ordinal)
    {
        "backfillDate", "batchId", "birthDate", "course", "discoveredFromId", "discoveredFromProvider",
        "discoveredFromType", "discoveryAncestors", "discoveryDepth", "domainRaceId", "name", "number",
        "date", "day", "distance", "entries", "layout", "meeting", "month", "observations", "observedAt",
        "owner", "position", "requestedByHorseId", "requestedByHorseName", "requestedByRaceId", "sex", "source",
        "sourceIdentity", "sourceUrl", "startTime", "trainer", "weekendPriorityUntil", "weight", "year",
    };
    private readonly DbContextOptions<CollectionPlatformDbContext> _dbOptions;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate;

    public CollectionPlatformStore(IOptions<CollectionPlatformOptions> options)
    {
        var value = options.Value;
        var directory = Path.GetFullPath(string.IsNullOrWhiteSpace(value.StateDirectory)
            ? "collection-platform-state" : value.StateDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, string.IsNullOrWhiteSpace(value.DatabaseFileName)
            ? "collection-platform.db" : value.DatabaseFileName);
        _dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=30").Options;
        _gate = Gates.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        using var db = CreateDbContext();
        CollectionPlatformSchemaMigrator.Migrate(db);
    }

    internal CollectionPlatformStore(DbContextOptions<CollectionPlatformDbContext> dbOptions)
    {
        _dbOptions = dbOptions;
        using (var context = new CollectionPlatformDbContext(dbOptions))
        {
            var key = context.Database.GetConnectionString() ?? Guid.NewGuid().ToString("N");
            _gate = Gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        }
        using var db = CreateDbContext();
        CollectionPlatformSchemaMigrator.Migrate(db);
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
                    DefinitionId = id.Value,
                    Name = name,
                    ResourceType = resourceType,
                    CurrentRevision = currentRevision,
                    Enabled = true,
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
                    DefinitionId = id.Value,
                    Revision = currentRevision,
                    Description = revisionDescription,
                    MayRequireRecollection = mayRequireRecollection,
                    CreatedAt = HorseRacingPrediction.Contracts.Time.JstTime.Now(),
                });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<int> GetCurrentRevisionAsync(CollectionDefinitionId definition,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        return await db.Definitions.AsNoTracking()
            .Where(x => x.DefinitionId == definition.Value && x.Enabled)
            .Select(x => (int?)x.CurrentRevision)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Collection definition {definition} is not registered.");
    }

    public async Task<CollectionRequestReceipt> RequestAsync(ResourceKey resource, CollectionDefinitionId definition,
        int requestedRevision, CollectionReason reason, DateTimeOffset requestedAt,
        CollectionLane lane = CollectionLane.Normal, int priority = (int)CollectionPriority.Normal,
        Uri? explicitUrl = null, string? batchId = null, DateOnly? effectiveDate = null,
        IReadOnlyDictionary<string, string>? attributes = null, CancellationToken cancellationToken = default,
        string? payloadFingerprint = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var receipt = await RequestCoreAsync(db, resource, definition, requestedRevision, reason, requestedAt,
                lane, priority, explicitUrl, batchId, effectiveDate, attributes, payloadFingerprint,
                cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return receipt;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            throw new InvalidOperationException("The resource already has an active collection task.", ex);
        }
        finally { _gate.Release(); }
    }

    private static async Task<CollectionRequestReceipt> RequestCoreAsync(CollectionPlatformDbContext db,
        ResourceKey resource, CollectionDefinitionId definition, int requestedRevision, CollectionReason reason,
        DateTimeOffset requestedAt, CollectionLane lane, int priority, Uri? explicitUrl, string? batchId,
        DateOnly? effectiveDate, IReadOnlyDictionary<string, string>? attributes, string? payloadFingerprint,
        CancellationToken cancellationToken)
    {
        resource = resource.Normalize();
        var metadataJson = SerializeTaskMetadata(attributes);
        if (string.IsNullOrWhiteSpace(resource.Provider) || string.IsNullOrWhiteSpace(resource.Id))
            throw new ArgumentException("Provider and resource id are required.", nameof(resource));
        if (requestedRevision < 1) throw new ArgumentOutOfRangeException(nameof(requestedRevision));
        CollectionHttpUrl.EnsureHttp(explicitUrl, nameof(explicitUrl));

        // Batch item identity is global to the supplied batch/item key, independent of the resource tuple.
        // This prevents a replay from silently rebinding an idempotency key to different content.
        if (!string.IsNullOrWhiteSpace(batchId) && payloadFingerprint is not null)
        {
            var keyedRequest = await db.Requests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.BatchId == batchId, cancellationToken).ConfigureAwait(false);
            if (keyedRequest is not null)
            {
                if (!string.Equals(keyedRequest.PayloadFingerprint, payloadFingerprint, StringComparison.Ordinal))
                    throw new CollectionRequestIdempotencyMismatchException(batchId);
                var keyedTask = await db.Tasks.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.RequestId == keyedRequest.RequestId, cancellationToken)
                    .ConfigureAwait(false);
                var keyedActive = await db.ActiveTasks.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.ResourcePk == keyedRequest.ResourcePk && x.DefinitionId == keyedRequest.DefinitionId,
                    cancellationToken).ConfigureAwait(false);
                return new(keyedRequest.RequestId,
                    keyedTask?.TaskId ?? keyedActive?.TaskId ?? Guid.Empty, false);
            }
        }

        var definitionEntity = await db.Definitions.SingleOrDefaultAsync(x => x.DefinitionId == definition.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Collection definition {definition} is not registered.");
        var suppression = await db.ResourceSuppressions.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Type == resource.Type && x.Provider == resource.Provider && x.ResourceId == resource.Id,
            cancellationToken).ConfigureAwait(false);
        if (suppression is not null)
            throw new CollectionResourceSuppressedException(resource, suppression.Reason);
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
                Type = resource.Type,
                Provider = resource.Provider,
                ResourceId = resource.Id,
                EffectiveDate = effectiveDate,
                AttributesJson = JsonSerializer.Serialize(attributes ?? new Dictionary<string, string>()),
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

        if (!string.IsNullOrWhiteSpace(batchId))
        {
            var existingRequest = await db.Requests.AsNoTracking().FirstOrDefaultAsync(x =>
                x.ResourcePk == resourceEntity.ResourcePk && x.DefinitionId == definition.Value
                && x.BatchId == batchId, cancellationToken).ConfigureAwait(false);
            if (existingRequest is not null)
            {
                if (payloadFingerprint is not null
                    && !string.Equals(existingRequest.PayloadFingerprint, payloadFingerprint,
                        StringComparison.Ordinal))
                    throw new CollectionRequestIdempotencyMismatchException(batchId);
                var existingTask = await db.Tasks.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.RequestId == existingRequest.RequestId, cancellationToken)
                    .ConfigureAwait(false);
                var existingActive = await db.ActiveTasks.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.ResourcePk == resourceEntity.ResourcePk && x.DefinitionId == definition.Value,
                    cancellationToken).ConfigureAwait(false);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new(existingRequest.RequestId,
                    existingTask?.TaskId ?? existingActive?.TaskId ?? Guid.Empty, false);
            }
        }

        if (IsOrdinaryRegistration(reason))
        {
            var activeTask = await (from activeRow in db.ActiveTasks
                                    join taskRow in db.Tasks on activeRow.TaskId equals taskRow.TaskId
                                    where activeRow.ResourcePk == resourceEntity.ResourcePk
                                          && activeRow.DefinitionId == definition.Value
                                    select taskRow).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (activeTask is not null)
            {
                activeTask.Lane = StrongerLane(activeTask.Lane, lane);
                activeTask.Priority = Math.Max(activeTask.Priority, priority);
                activeTask.UpdatedAt = requestedAt;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new(activeTask.RequestId, activeTask.TaskId, false);
            }

            var existingTask = await db.Tasks.AsNoTracking()
                .Where(x => x.ResourcePk == resourceEntity.ResourcePk
                    && x.DefinitionId == definition.Value
                    && x.RequestedRevision == requestedRevision)
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.TaskId)
                .Select(x => new { x.TaskId, x.RequestId })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (existingTask is not null)
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new(existingTask.RequestId, existingTask.TaskId, false);
            }
        }

        var request = new CollectionRequestEntity
        {
            RequestId = Guid.NewGuid(),
            ResourcePk = resourceEntity.ResourcePk,
            DefinitionId = definition.Value,
            RequestedRevision = requestedRevision,
            Reason = reason,
            RequestedAt = requestedAt,
            ExplicitUrl = explicitUrl?.AbsoluteUri,
            BatchId = batchId,
            PayloadFingerprint = payloadFingerprint,
        };
        db.Requests.Add(request);

        var active = await db.ActiveTasks.SingleOrDefaultAsync(x => x.ResourcePk == resourceEntity.ResourcePk
            && x.DefinitionId == definition.Value, cancellationToken);
        if (active is not null)
        {
            var activeStatus = await db.Tasks.Where(x => x.TaskId == active.TaskId)
                .Select(x => (CollectionTaskStatus?)x.Status).SingleOrDefaultAsync(cancellationToken);
            if (activeStatus is null || activeStatus is CollectionTaskStatus.Succeeded
                or CollectionTaskStatus.Failed or CollectionTaskStatus.Cancelled or CollectionTaskStatus.DeadLetter)
            {
                db.ActiveTasks.Remove(active);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                active = null;
            }
        }
        if (active is not null)
        {
            if (reason is CollectionReason.Recovery or CollectionReason.ManualRefresh
                or CollectionReason.PeriodRecollection)
                await StartFailureRecoveryAsync(db, resourceEntity.ResourcePk, definition.Value,
                    active.TaskId, requestedAt, cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new CollectionRequestReceipt(request.RequestId, active.TaskId, false);
        }

        var task = new CollectionTaskEntity
        {
            TaskId = Guid.NewGuid(),
            RequestId = request.RequestId,
            ResourcePk = resourceEntity.ResourcePk,
            DefinitionId = definition.Value,
            RequestedRevision = requestedRevision,
            Status = CollectionTaskStatus.Ready,
            Lane = lane,
            Priority = priority,
            AvailableAt = requestedAt,
            CreatedAt = requestedAt,
            UpdatedAt = requestedAt,
            DispatchGeneration = 1,
            MetadataJson = metadataJson,
        };
        db.Tasks.Add(task);
        db.ActiveTasks.Add(new CollectionActiveTaskEntity
        { ResourcePk = resourceEntity.ResourcePk, DefinitionId = definition.Value, TaskId = task.TaskId });
        db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
        {
            OutboxId = Guid.NewGuid(),
            TaskId = task.TaskId,
            DispatchGeneration = task.DispatchGeneration,
            AvailableAt = requestedAt,
            CreatedAt = requestedAt,
        });
        if (reason is CollectionReason.Recovery or CollectionReason.ManualRefresh
            or CollectionReason.PeriodRecollection)
            await StartFailureRecoveryAsync(db, resourceEntity.ResourcePk, definition.Value,
                task.TaskId, requestedAt, cancellationToken).ConfigureAwait(false);
        var state = await db.States.SingleOrDefaultAsync(x => x.ResourcePk == resourceEntity.ResourcePk
            && x.DefinitionId == definition.Value, cancellationToken);
        if (state is null)
            db.States.Add(new CollectionStateEntity
            {
                ResourcePk = resourceEntity.ResourcePk,
                DefinitionId = definition.Value,
                RequiredRevision = requestedRevision,
                Status = CollectionStateStatus.Pending,
                UpdatedAt = requestedAt,
            });
        else
        {
            state.RequiredRevision = Math.Max(state.RequiredRevision, requestedRevision);
            state.Status = CollectionStateStatus.Pending;
            state.UpdatedAt = requestedAt;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CollectionRequestReceipt(request.RequestId, task.TaskId, true);
    }

    private static CollectionLane StrongerLane(CollectionLane left, CollectionLane right) =>
        (CollectionLane)Math.Min((int)left, (int)right);

    private static bool IsOrdinaryRegistration(CollectionReason reason)
        => reason is CollectionReason.Initial or CollectionReason.Backfill or CollectionReason.Discovery;

    public async Task<CollectionBulkPreview> PreviewBulkRequestAsync(CollectionDefinitionId definition,
        int requestedRevision, IEnumerable<CollectionBulkTarget> targets,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeBulkTargets(targets);
        await using var db = CreateDbContext();
        await ValidateBulkRequestAsync(db, definition, requestedRevision, normalized, cancellationToken)
            .ConfigureAwait(false);
        return new(definition, requestedRevision, normalized.Count, normalized.Select(x => x.Resource).ToList());
    }

    public async Task<CollectionBulkExecution> ExecuteBulkRequestAsync(CollectionDefinitionId definition,
        int requestedRevision, CollectionReason reason, IEnumerable<CollectionBulkTarget> targets,
        DateTimeOffset requestedAt, string batchId, CollectionLane lane, int priority,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(batchId)) throw new ArgumentException("Batch id is required.", nameof(batchId));
        var normalized = NormalizeBulkTargets(targets);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await ValidateBulkRequestAsync(db, definition, requestedRevision, normalized, cancellationToken)
                .ConfigureAwait(false);
            var suppressions = await db.ResourceSuppressions.AsNoTracking().ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var suppressed = normalized.FirstOrDefault(target => suppressions.Any(x =>
                x.Type == target.Resource.Type && x.Provider == target.Resource.Provider
                && x.ResourceId == target.Resource.Id));
            if (suppressed is not null)
            {
                var suppressionReason = suppressions.Single(x => x.Type == suppressed.Resource.Type
                    && x.Provider == suppressed.Resource.Provider && x.ResourceId == suppressed.Resource.Id).Reason;
                throw new CollectionResourceSuppressedException(suppressed.Resource, suppressionReason);
            }
            var receipts = new List<CollectionRequestReceipt>(normalized.Count);
            var created = 0;
            foreach (var target in normalized)
            {
                var metadataJson = SerializeTaskMetadata(target.Attributes);
                var resource = await db.Resources.SingleOrDefaultAsync(x => x.Type == target.Resource.Type
                    && x.Provider == target.Resource.Provider && x.ResourceId == target.Resource.Id, cancellationToken);
                if (resource is null)
                {
                    resource = new CollectionResourceEntity
                    {
                        Type = target.Resource.Type,
                        Provider = target.Resource.Provider,
                        ResourceId = target.Resource.Id,
                        EffectiveDate = target.EffectiveDate,
                        AttributesJson = JsonSerializer.Serialize(target.Attributes ?? new Dictionary<string, string>()),
                        CreatedAt = requestedAt,
                    };
                    db.Resources.Add(resource);
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                var duplicate = await db.Requests.FirstOrDefaultAsync(x => x.ResourcePk == resource.ResourcePk
                    && x.DefinitionId == definition.Value && x.BatchId == batchId, cancellationToken);
                if (duplicate is not null)
                {
                    var duplicateTask = await db.Tasks.FirstOrDefaultAsync(x => x.RequestId == duplicate.RequestId,
                        cancellationToken);
                    receipts.Add(new(duplicate.RequestId, duplicateTask?.TaskId ?? Guid.Empty, false));
                    continue;
                }
                var request = new CollectionRequestEntity
                {
                    RequestId = Guid.NewGuid(),
                    ResourcePk = resource.ResourcePk,
                    DefinitionId = definition.Value,
                    RequestedRevision = requestedRevision,
                    Reason = reason,
                    RequestedAt = requestedAt,
                    BatchId = batchId,
                };
                db.Requests.Add(request);
                var active = await db.ActiveTasks.FirstOrDefaultAsync(x => x.ResourcePk == resource.ResourcePk
                    && x.DefinitionId == definition.Value, cancellationToken);
                Guid taskId;
                var createdTask = active is null;
                if (active is null)
                {
                    var task = new CollectionTaskEntity
                    {
                        TaskId = Guid.NewGuid(),
                        RequestId = request.RequestId,
                        ResourcePk = resource.ResourcePk,
                        DefinitionId = definition.Value,
                        RequestedRevision = requestedRevision,
                        Status = CollectionTaskStatus.Ready,
                        Lane = lane,
                        Priority = priority,
                        AvailableAt = requestedAt,
                        CreatedAt = requestedAt,
                        UpdatedAt = requestedAt,
                        DispatchGeneration = 1,
                        MetadataJson = metadataJson,
                    };
                    taskId = task.TaskId;
                    db.Tasks.Add(task);
                    db.ActiveTasks.Add(new CollectionActiveTaskEntity
                    { ResourcePk = resource.ResourcePk, DefinitionId = definition.Value, TaskId = taskId });
                    db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
                    {
                        OutboxId = Guid.NewGuid(),
                        TaskId = taskId,
                        DispatchGeneration = 1,
                        AvailableAt = requestedAt,
                        CreatedAt = requestedAt,
                    });
                    created++;
                }
                else taskId = active.TaskId;
                var state = await db.States.FirstOrDefaultAsync(x => x.ResourcePk == resource.ResourcePk
                    && x.DefinitionId == definition.Value, cancellationToken);
                if (state is null)
                    db.States.Add(new CollectionStateEntity
                    {
                        ResourcePk = resource.ResourcePk,
                        DefinitionId = definition.Value,
                        RequiredRevision = requestedRevision,
                        Status = CollectionStateStatus.Pending,
                        UpdatedAt = requestedAt,
                    });
                else
                {
                    state.RequiredRevision = Math.Max(state.RequiredRevision, requestedRevision);
                    if (createdTask) state.Status = CollectionStateStatus.Pending;
                    state.UpdatedAt = requestedAt;
                }
                receipts.Add(new(request.RequestId, taskId, createdTask));
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(batchId, normalized.Count, created, receipts);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<CollectionBulkTarget>> SelectBulkTargetsAsync(CollectionDefinitionId definition,
        DateTimeOffset? lastCollectedBefore = null, CollectionStateStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var rows = await (from state in db.States.AsNoTracking()
                          join resource in db.Resources.AsNoTracking() on state.ResourcePk equals resource.ResourcePk
                          where state.DefinitionId == definition.Value
                                && !db.ResourceSuppressions.Any(x => x.Type == resource.Type
                                    && x.Provider == resource.Provider && x.ResourceId == resource.ResourceId)
                          select new { state, resource }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Where(x => (!lastCollectedBefore.HasValue || x.state.LastCollectedAt <= lastCollectedBefore
                                || x.state.LastCollectedAt == null)
                               && (!status.HasValue || x.state.Status == status))
            .Select(x => new CollectionBulkTarget(new(x.resource.Type, x.resource.Provider, x.resource.ResourceId),
                x.resource.EffectiveDate,
                JsonSerializer.Deserialize<Dictionary<string, string>>(x.resource.AttributesJson) ?? []))
            .ToList();
    }

    public async Task<LeasedCollectionTask?> AcquireAsync(Guid taskId, long dispatchGeneration,
        DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        => await AcquireAsync(taskId, dispatchGeneration, now, leaseDuration, null, cancellationToken)
            .ConfigureAwait(false);

    public async Task<LeasedCollectionTask?> AcquireAsync(Guid taskId, long dispatchGeneration,
        DateTimeOffset now, TimeSpan leaseDuration, CollectionAttemptCorrelation? correlation,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (await db.Controls.AnyAsync(x => x.ControlId == "pipeline" && x.IsPaused, cancellationToken))
                return null;
            await ReclaimExpiredAsync(db, now, cancellationToken).ConfigureAwait(false);
            var task = await db.Tasks.SingleOrDefaultAsync(x => x.TaskId == taskId, cancellationToken);
            if (task is null || task.Status != CollectionTaskStatus.Ready || task.AvailableAt > now
                || task.DispatchGeneration != dispatchGeneration) return null;
            var request = await db.Requests.SingleAsync(x => x.RequestId == task.RequestId, cancellationToken);
            var resource = await db.Resources.SingleAsync(x => x.ResourcePk == task.ResourcePk, cancellationToken);
            var locations = await db.Locations.AsNoTracking().Where(x => x.ResourcePk == task.ResourcePk
                && x.DefinitionId == task.DefinitionId && x.Status != ResourceLocationStatus.Invalid)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var candidates = locations.OrderByDescending(x => x.Status == ResourceLocationStatus.Active)
                .ThenByDescending(x => x.LastVerifiedAt)
                .Select(x => (Location: x, Valid: CollectionHttpUrl.TryCreate(x.Url, out var url), Url: url))
                .Where(x => x.Valid)
                .Select(x => new ResourceLocationCandidate(x.Location.LocationId, x.Url!, x.Location.Source,
                    x.Location.Status, x.Location.LastVerifiedAt)).ToList();
            if (CollectionHttpUrl.TryCreate(request.ExplicitUrl, out var explicitUrl))
                candidates.Insert(0, new(0, explicitUrl!, ResourceLocationSource.Explicit,
                    ResourceLocationStatus.Unknown, null));
            task.Status = CollectionTaskStatus.Running;
            task.LeaseToken = Guid.NewGuid().ToString("N");
            task.LeaseExpiresAt = now.Add(leaseDuration);
            task.StartedAt ??= now;
            task.UpdatedAt = now;
            task.AttemptCount++;
            db.Attempts.Add(new CollectionAttemptEntity
            {
                AttemptId = Guid.NewGuid(),
                TaskId = task.TaskId,
                AttemptNumber = task.AttemptCount,
                StartedAt = now,
                Result = CollectionAttemptResult.Running,
                ExecutionBatchId = correlation?.ExecutionBatchId,
                DispatchEnvelopeId = correlation?.DispatchEnvelopeId,
                QueueMessageId = correlation?.QueueMessageId,
                LambdaRequestId = correlation?.LambdaRequestId,
                BatchTaskOrdinal = correlation?.BatchTaskOrdinal,
                BatchTaskCount = correlation?.BatchTaskCount,
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
                DeserializeTaskMetadata(task.MetadataJson ?? resource.AttributesJson), candidates);
        }
        finally { _gate.Release(); }
    }

    public async Task<CollectionTaskAcquireStatus> ClassifyAcquireFailureAsync(Guid taskId, long dispatchGeneration,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var task = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(x => x.TaskId == taskId, cancellationToken)
            .ConfigureAwait(false);
        if (task is null) return CollectionTaskAcquireStatus.ActiveElsewhere;
        if (task.DispatchGeneration != dispatchGeneration) return CollectionTaskAcquireStatus.SupersededGeneration;
        if (task.Status is CollectionTaskStatus.Succeeded or CollectionTaskStatus.Failed
            or CollectionTaskStatus.Cancelled or CollectionTaskStatus.DeadLetter)
            return CollectionTaskAcquireStatus.AlreadyTerminal;
        return CollectionTaskAcquireStatus.ActiveElsewhere;
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
            var locationOutcomes = await ValidateLocationOutcomesAsync(db, task, completion.LocationOutcomes,
                cancellationToken).ConfigureAwait(false);
            if (locationOutcomes is null || completion.Result == CollectionAttemptResult.Running) return false;
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
            foreach (var outcome in locationOutcomes)
            {
                var candidate = await db.Locations.SingleAsync(x => x.LocationId == outcome.LocationId,
                    cancellationToken);
                ApplyLocationOutcome(candidate, outcome.Result, now, outcome.ErrorCode);
            }
            if (completion.RequestedUrl is not null)
            {
                var requested = completion.RequestedUrl.AbsoluteUri;
                var location = await db.Locations.SingleOrDefaultAsync(x => x.ResourcePk == task.ResourcePk
                    && x.DefinitionId == task.DefinitionId && x.Url == requested, cancellationToken);
                if (location is null)
                {
                    location = new ResourceLocationEntity
                    {
                        ResourcePk = task.ResourcePk,
                        DefinitionId = task.DefinitionId,
                        Url = requested,
                        Source = ResourceLocationSource.Explicit,
                        Status = ResourceLocationStatus.Unknown,
                        DiscoveredAt = now,
                    };
                    db.Locations.Add(location);
                }
                if (completion.Result == CollectionAttemptResult.Succeeded)
                {
                    location.Status = ResourceLocationStatus.Active;
                    location.LastVerifiedAt = now;
                    location.LastFailureCode = null;
                }
                else if (completion.Result is CollectionAttemptResult.ResourceNotFound
                         or CollectionAttemptResult.UnexpectedPage or CollectionAttemptResult.ValidationFailure)
                {
                    location.Status = ResourceLocationStatus.Suspect;
                    location.LastFailedAt = now;
                    location.LastFailureCode = completion.ErrorCode ?? completion.Result.ToString();
                }
            }
            if (completion.Result == CollectionAttemptResult.Succeeded && completion.FinalUrl is not null
                && completion.FinalUrl != completion.RequestedUrl)
            {
                var redirected = completion.FinalUrl.AbsoluteUri;
                var location = await db.Locations.SingleOrDefaultAsync(x => x.ResourcePk == task.ResourcePk
                    && x.DefinitionId == task.DefinitionId && x.Url == redirected, cancellationToken);
                if (location is null)
                    db.Locations.Add(new ResourceLocationEntity
                    {
                        ResourcePk = task.ResourcePk,
                        DefinitionId = task.DefinitionId,
                        Url = redirected,
                        Source = ResourceLocationSource.Redirected,
                        Status = ResourceLocationStatus.Active,
                        DiscoveredAt = now,
                        LastVerifiedAt = now,
                    });
                else { location.Status = ResourceLocationStatus.Active; location.LastVerifiedAt = now; }
            }
            task.LeaseToken = null;
            task.LeaseExpiresAt = null;
            task.UpdatedAt = now;
            var state = await db.States.SingleAsync(x => x.ResourcePk == task.ResourcePk
                && x.DefinitionId == task.DefinitionId, cancellationToken);

            if (task.CancellationRequestedAt.HasValue || completion.Result == CollectionAttemptResult.Cancelled)
            {
                task.Status = CollectionTaskStatus.Cancelled;
                task.FinishedAt = now;
                var suppressed = await IsResourceSuppressedAsync(db, task.ResourcePk, cancellationToken)
                    .ConfigureAwait(false);
                state.Status = suppressed ? CollectionStateStatus.Unavailable : CollectionStateStatus.Unknown;
                if (suppressed) state.NextCollectionAt = null;
                db.ActiveTasks.Remove(await db.ActiveTasks.SingleAsync(x => x.TaskId == taskId, cancellationToken));
                if (!suppressed && await ReopenFailuresAsync(db, taskId, cancellationToken).ConfigureAwait(false) > 0)
                    state.Status = CollectionStateStatus.Failed;
            }
            else if (completion.Result == CollectionAttemptResult.Succeeded)
            {
                task.Status = CollectionTaskStatus.Succeeded;
                task.FinishedAt = now;
                state.AppliedRevision = Math.Max(state.AppliedRevision, task.RequestedRevision);
                state.LastCollectedAt = now;
                state.NextCollectionAt = completion.NextCollectionAt;
                db.ActiveTasks.Remove(await db.ActiveTasks.SingleAsync(x => x.TaskId == taskId, cancellationToken));
                state.Status = state.AppliedRevision >= state.RequiredRevision
                    ? CollectionStateStatus.Current : CollectionStateStatus.Stale;
                if (state.Status == CollectionStateStatus.Current)
                    await ResolveFailuresAsync(db, task.ResourcePk, task.DefinitionId, now, cancellationToken)
                        .ConfigureAwait(false);
                if (state.AppliedRevision < state.RequiredRevision)
                {
                    var followUpRequests = await db.Requests
                        .Where(x => x.ResourcePk == task.ResourcePk && x.DefinitionId == task.DefinitionId
                                    && x.RequestedRevision >= state.RequiredRevision)
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    var followUpRequest = followUpRequests.OrderByDescending(x => x.RequestedAt).FirstOrDefault();
                    if (followUpRequest is not null)
                    {
                        var followUp = new CollectionTaskEntity
                        {
                            TaskId = Guid.NewGuid(),
                            RequestId = followUpRequest.RequestId,
                            ResourcePk = task.ResourcePk,
                            DefinitionId = task.DefinitionId,
                            RequestedRevision = state.RequiredRevision,
                            Status = CollectionTaskStatus.Ready,
                            Lane = task.Lane,
                            Priority = task.Priority,
                            AvailableAt = now,
                            CreatedAt = now,
                            UpdatedAt = now,
                            DispatchGeneration = 1,
                            MetadataJson = task.MetadataJson,
                        };
                        db.Tasks.Add(followUp);
                        db.ActiveTasks.Add(new CollectionActiveTaskEntity
                        { ResourcePk = task.ResourcePk, DefinitionId = task.DefinitionId, TaskId = followUp.TaskId });
                        db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
                        {
                            OutboxId = Guid.NewGuid(),
                            TaskId = followUp.TaskId,
                            DispatchGeneration = 1,
                            AvailableAt = now,
                            CreatedAt = now,
                        });
                        state.Status = CollectionStateStatus.Pending;
                    }
                }
            }
            else if (completion.Result == CollectionAttemptResult.NotApplicable)
            {
                task.Status = CollectionTaskStatus.Succeeded;
                task.FinishedAt = now;
                state.LastCollectedAt = now;
                state.NextCollectionAt = null;
                state.Status = CollectionStateStatus.Unavailable;
                db.ActiveTasks.Remove(await db.ActiveTasks.SingleAsync(x => x.TaskId == taskId, cancellationToken));
                await ResolveFailuresAsync(db, task.ResourcePk, task.DefinitionId, now, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (IsRetryable(completion.Result) || completion.RetryAt.HasValue)
            {
                task.Status = CollectionTaskStatus.RetryWaiting;
                task.AvailableAt = completion.RetryAt ?? now.Add(DefaultRetryDelay(completion.Result, task.AttemptCount));
                task.DispatchGeneration++;
                state.NextCollectionAt = task.AvailableAt;
                state.Status = CollectionStateStatus.Pending;
                db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
                {
                    OutboxId = Guid.NewGuid(),
                    TaskId = task.TaskId,
                    DispatchGeneration = task.DispatchGeneration,
                    AvailableAt = task.AvailableAt,
                    CreatedAt = now,
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
                await QueueFailureNotificationAsync(db, task, completion.ErrorCode, completion.ErrorMessage, now,
                    pausePipeline: completion.Result != CollectionAttemptResult.ResourceNotFound
                        && completion.FailureImpact != CollectionFailureImpact.Isolated,
                    cancellationToken).ConfigureAwait(false);
            }
            state.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    private static async Task<IReadOnlyList<ResourceLocationOutcome>?> ValidateLocationOutcomesAsync(
        CollectionPlatformDbContext db, CollectionTaskEntity task,
        IReadOnlyList<ResourceLocationOutcome>? outcomes, CancellationToken cancellationToken)
    {
        if (outcomes is null or { Count: 0 }) return [];
        if (outcomes.Count > MaxLocationOutcomesPerCompletion
            || outcomes.Any(x => x.LocationId < 0 || x.Result == CollectionAttemptResult.Running)) return null;

        var normalized = new List<ResourceLocationOutcome>();
        // LocationId=0 represents an explicit URL that has not been persisted as a
        // ResourceLocation yet. RequestedUrl below promotes it after a successful
        // collection, so it has no existing location row to validate or update here.
        foreach (var group in outcomes.Where(x => x.LocationId > 0).GroupBy(x => x.LocationId))
        {
            var first = group.First();
            if (group.Any(x => x.Result != first.Result
                || !string.Equals(x.ErrorCode, first.ErrorCode, StringComparison.Ordinal))) return null;
            normalized.Add(first);
        }

        var ids = normalized.Select(x => x.LocationId).ToList();
        var validCount = await db.Locations.CountAsync(x => ids.Contains(x.LocationId)
            && x.ResourcePk == task.ResourcePk && x.DefinitionId == task.DefinitionId, cancellationToken)
            .ConfigureAwait(false);
        return validCount == ids.Count ? normalized : null;
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

    public async Task<bool> HasActiveRaceMutationAsync(string raceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(raceId)) return false;
        await using var db = CreateDbContext();
        var resources = await (from active in db.ActiveTasks.AsNoTracking()
                               join resource in db.Resources.AsNoTracking() on active.ResourcePk equals resource.ResourcePk
                               select new { resource.ResourceId, resource.AttributesJson }).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return resources.Any(x => string.Equals(x.ResourceId, raceId, StringComparison.Ordinal)
            || (JsonSerializer.Deserialize<Dictionary<string, string>>(x.AttributesJson) is { } attributes
                && attributes.TryGetValue("domainRaceId", out var domainRaceId)
                && string.Equals(domainRaceId, raceId, StringComparison.Ordinal)));
    }

    public async Task<bool> IsValidActiveRaceLeaseAsync(Guid taskId, string leaseToken, string raceId,
        CancellationToken cancellationToken = default)
    {
        if (taskId == Guid.Empty || string.IsNullOrWhiteSpace(leaseToken) || string.IsNullOrWhiteSpace(raceId))
            return false;
        await using var db = CreateDbContext();
        var row = await (from active in db.ActiveTasks.AsNoTracking()
                         join task in db.Tasks.AsNoTracking() on active.TaskId equals task.TaskId
                         join resource in db.Resources.AsNoTracking() on active.ResourcePk equals resource.ResourcePk
                         where task.TaskId == taskId && task.Status == CollectionTaskStatus.Running
                             && task.LeaseToken == leaseToken
                         select new { resource.ResourceId, resource.AttributesJson, task.LeaseExpiresAt }).SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row?.LeaseExpiresAt is null || row.LeaseExpiresAt <= HorseRacingPrediction.Contracts.Time.JstTime.Now()) return false;
        var attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(row.AttributesJson);
        return string.Equals(row.ResourceId, raceId, StringComparison.Ordinal)
            || (attributes?.TryGetValue("domainRaceId", out var domainRaceId) == true
                && string.Equals(domainRaceId, raceId, StringComparison.Ordinal));
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

    public async Task<CollectionResourceDetail?> GetResourceDetailAsync(ResourceKey resource,
        CollectionDefinitionId definition, int historyPage = 1, int historyPageSize = 25,
        CancellationToken cancellationToken = default)
        => await GetResourceDetailPagedAsync(resource, definition, historyPage, historyPage, historyPage,
            historyPageSize, cancellationToken).ConfigureAwait(false);

    public async Task<CollectionResourceDetail?> GetResourceDetailPagedAsync(ResourceKey resource,
        CollectionDefinitionId definition, int requestHistoryPage = 1, int taskHistoryPage = 1,
        int attemptHistoryPage = 1, int historyPageSize = 25, CancellationToken cancellationToken = default)
    {
        resource = resource.Normalize();
        await using var db = CreateDbContext();
        var item = await db.Resources.AsNoTracking().SingleOrDefaultAsync(x => x.Type == resource.Type
            && x.Provider == resource.Provider && x.ResourceId == resource.Id, cancellationToken);
        if (item is null) return null;
        var stateEntity = await db.States.AsNoTracking().SingleOrDefaultAsync(x => x.ResourcePk == item.ResourcePk
            && x.DefinitionId == definition.Value, cancellationToken);
        var locationRows = await db.Locations.AsNoTracking().Where(x => x.ResourcePk == item.ResourcePk
                && x.DefinitionId == definition.Value).ToListAsync(cancellationToken);
        var locations = locationRows.OrderByDescending(x => x.LastVerifiedAt ?? x.DiscoveredAt)
            .Select(x => new ResourceLocationCandidate(x.LocationId, new Uri(x.Url),
            x.Source, x.Status, x.LastVerifiedAt)).ToList();
        requestHistoryPage = Math.Max(1, requestHistoryPage);
        taskHistoryPage = Math.Max(1, taskHistoryPage);
        attemptHistoryPage = Math.Max(1, attemptHistoryPage);
        historyPageSize = Math.Clamp(historyPageSize, 1, 100);
        static int Offset(int page, int pageSize)
        {
            var offset = ((long)page - 1) * pageSize;
            return offset >= int.MaxValue ? int.MaxValue : (int)offset;
        }
        var requestQuery = db.Requests.AsNoTracking().Where(x => x.ResourcePk == item.ResourcePk
            && x.DefinitionId == definition.Value);
        var requestTotal = await requestQuery.CountAsync(cancellationToken);
        var requestRows = await requestQuery.OrderByDescending(x => x.RequestedAt)
            .ThenByDescending(x => x.RequestId)
            .Skip(Offset(requestHistoryPage, historyPageSize)).Take(historyPageSize).ToListAsync(cancellationToken);
        var requests = requestRows.Select(x =>
            new CollectionRequestSummary(x.RequestId, x.RequestedRevision, x.Reason, x.RequestedAt,
                x.ExplicitUrl, x.BatchId)).ToList();
        var taskQuery = db.Tasks.AsNoTracking().Where(x => x.ResourcePk == item.ResourcePk
            && x.DefinitionId == definition.Value);
        var taskTotal = await taskQuery.CountAsync(cancellationToken);
        var latestTaskRow = await taskQuery.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.TaskId)
            .FirstOrDefaultAsync(cancellationToken);
        var taskRows = await taskQuery.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.TaskId)
            .Skip(Offset(taskHistoryPage, historyPageSize)).Take(historyPageSize).ToListAsync(cancellationToken);
        var tasks = taskRows.Select(x => new CollectionTaskSummary(x.TaskId, resource, definition, x.Status,
            x.Lane, x.Priority, x.RequestedRevision, x.AvailableAt, x.AttemptCount,
            DeserializeTaskMetadata(x.MetadataJson ?? item.AttributesJson))).ToList();
        var taskIds = taskQuery.Select(x => x.TaskId);
        var attemptQuery = db.Attempts.AsNoTracking().Where(x => taskIds.Contains(x.TaskId));
        var attemptTotal = await attemptQuery.CountAsync(cancellationToken);
        var attemptRows = await attemptQuery.OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.AttemptId)
            .Skip(Offset(attemptHistoryPage, historyPageSize)).Take(historyPageSize).ToListAsync(cancellationToken);
        var attempts = attemptRows.Select(x => new CollectionAttemptSummary(x.AttemptId, x.TaskId,
                x.AttemptNumber, x.StartedAt, x.FinishedAt, x.Result, x.ErrorCode, x.ErrorMessage,
                x.RequestedUrl, x.FinalUrl, x.HttpStatusCode, x.PageIdentification, x.ExecutionBatchId,
                x.DispatchEnvelopeId, x.QueueMessageId, x.LambdaRequestId, x.BatchTaskOrdinal,
                x.BatchTaskCount)).ToList();
        var state = stateEntity is null ? null : new CollectionStateSnapshot(resource, definition,
            stateEntity.AppliedRevision, stateEntity.RequiredRevision, stateEntity.LastCollectedAt,
            stateEntity.NextCollectionAt, stateEntity.Status);
        var latestTask = latestTaskRow is null ? null : new CollectionTaskSummary(latestTaskRow.TaskId,
            resource, definition, latestTaskRow.Status, latestTaskRow.Lane, latestTaskRow.Priority,
            latestTaskRow.RequestedRevision, latestTaskRow.AvailableAt, latestTaskRow.AttemptCount,
            DeserializeTaskMetadata(latestTaskRow.MetadataJson ?? item.AttributesJson));
        var failureRows = await FailureQuery(db, item.ResourcePk, definition.Value)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        failureRows = failureRows.OrderByDescending(x => x.Notification.FailedAt).ToList();
        var failures = failureRows.Select(ToFailure).ToList();
        return new(state, locations, requests, tasks, attempts, requestTotal, taskTotal,
            attemptTotal, requestHistoryPage, historyPageSize, latestTask, taskHistoryPage, attemptHistoryPage,
            failures);
    }

    public async Task<CollectionExecutionBatchDetail?> GetExecutionBatchAsync(Guid executionBatchId,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var rows = await (from attempt in db.Attempts.AsNoTracking()
                          join task in db.Tasks.AsNoTracking() on attempt.TaskId equals task.TaskId
                          join resource in db.Resources.AsNoTracking() on task.ResourcePk equals resource.ResourcePk
                          where attempt.ExecutionBatchId == executionBatchId
                          select new { attempt, task, resource }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0) return null;

        var ordered = rows.OrderBy(x => x.attempt.BatchTaskOrdinal ?? int.MaxValue)
            .ThenBy(x => x.attempt.StartedAt).ThenBy(x => x.task.TaskId).ToList();
        var first = ordered[0].attempt;
        var finishedAt = ordered.All(x => x.attempt.FinishedAt.HasValue)
            ? ordered.Max(x => x.attempt.FinishedAt)
            : null;
        var tasks = ordered.Select(x => new CollectionExecutionBatchTaskSummary(x.task.TaskId,
            new ResourceKey(x.resource.Type, x.resource.Provider, x.resource.ResourceId),
            new CollectionDefinitionId(x.task.DefinitionId), x.task.Status, x.attempt.Result,
            x.attempt.AttemptNumber, x.attempt.BatchTaskOrdinal ?? 0, x.attempt.StartedAt,
            x.attempt.FinishedAt)).ToList();
        return new(executionBatchId, first.DispatchEnvelopeId ?? Guid.Empty, first.QueueMessageId ?? string.Empty,
            first.LambdaRequestId, first.BatchTaskCount ?? tasks.Count, ordered.Min(x => x.attempt.StartedAt),
            finishedAt, tasks);
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
                DefinitionId = definition.Value,
                Revision = revision,
                Description = description,
                MayRequireRecollection = true,
                CreatedAt = now,
            });
            db.RevisionImpacts.Add(new CollectionRevisionImpactEntity
            {
                DefinitionId = definition.Value,
                Revision = revision,
                ScopeType = impact.ScopeType,
                ScopePayload = impact.ScopePayload,
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

    public async Task<RevisionImpactPreview> PreviewRevisionImpactAsync(CollectionDefinitionId definition,
        int revision, RevisionImpact impact, IEnumerable<INamedRevisionImpactCondition> namedConditions,
        CancellationToken cancellationToken = default)
    {
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        var conditions = namedConditions.ToDictionary(x => x.Name, StringComparer.Ordinal);
        ValidateImpact(impact, conditions.Values);
        await using var db = CreateDbContext();
        var definitionEntity = await db.Definitions.AsNoTracking()
            .SingleAsync(x => x.DefinitionId == definition.Value, cancellationToken).ConfigureAwait(false);
        if (await db.Revisions.AsNoTracking().AnyAsync(x => x.DefinitionId == definition.Value
                && x.Revision == revision, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"Revision {definition}:{revision} already exists.");
        var candidates = await LoadRevisionCandidatesAsync(db, definitionEntity.DefinitionId, cancellationToken)
            .ConfigureAwait(false);
        var affected = candidates.Where(x => MatchesImpact(x, impact, conditions)).Select(x => x.Resource).ToList();
        return new(definition, revision, impact, candidates.Count, affected);
    }

    public async Task<RevisionRecollectionExpansion> ExpandRevisionRecollectionAsync(
        CollectionDefinitionId definition, int revision,
        IEnumerable<INamedRevisionImpactCondition> namedConditions, DateTimeOffset now,
        CollectionLane lane = CollectionLane.Background, int priority = (int)CollectionPriority.Background,
        CancellationToken cancellationToken = default)
    {
        RevisionImpact impact;
        List<RevisionResourceCandidate> candidates;
        await using (var db = CreateDbContext())
        {
            var row = await db.RevisionImpacts.AsNoTracking().SingleAsync(x =>
                x.DefinitionId == definition.Value && x.Revision == revision, cancellationToken).ConfigureAwait(false);
            impact = new(row.ScopeType, row.ScopePayload);
            candidates = await LoadRevisionCandidatesAsync(db, definition.Value, cancellationToken).ConfigureAwait(false);
        }
        var conditions = namedConditions.ToDictionary(x => x.Name, StringComparer.Ordinal);
        ValidateImpact(impact, conditions.Values);
        var affected = candidates.Where(x => MatchesImpact(x, impact, conditions)).ToList();
        var batchId = $"revision:{definition.Value}:{revision}";
        var created = 0;
        foreach (var candidate in affected)
        {
            var receipt = await RequestAsync(candidate.Resource, definition, revision,
                CollectionReason.DefinitionChanged, now, lane, priority, batchId: batchId,
                effectiveDate: candidate.EffectiveDate, attributes: candidate.Attributes,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (receipt.CreatedTask) created++;
        }
        return new(definition, revision, batchId, affected.Count, created, affected.Count - created);
    }

    public async Task<RevisionRecollectionProgress> GetRevisionRecollectionProgressAsync(
        CollectionDefinitionId definition, int revision,
        IEnumerable<INamedRevisionImpactCondition> namedConditions,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var impactRow = await db.RevisionImpacts.AsNoTracking().SingleAsync(x =>
            x.DefinitionId == definition.Value && x.Revision == revision, cancellationToken).ConfigureAwait(false);
        var impact = new RevisionImpact(impactRow.ScopeType, impactRow.ScopePayload);
        var conditions = namedConditions.ToDictionary(x => x.Name, StringComparer.Ordinal);
        ValidateImpact(impact, conditions.Values);
        var rows = await (from state in db.States.AsNoTracking()
                          join resource in db.Resources.AsNoTracking() on state.ResourcePk equals resource.ResourcePk
                          where state.DefinitionId == definition.Value
                          select new { state, resource }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var affected = rows.Where(x => MatchesImpact(ToRevisionCandidate(x.resource), impact, conditions)).ToList();
        var completed = affected.Count(x => x.state.AppliedRevision >= revision);
        var failed = affected.Count(x => x.state.AppliedRevision < revision
                                         && x.state.Status == CollectionStateStatus.Failed);
        return new(definition, revision, affected.Count, completed, affected.Count - completed - failed, failed);
    }

    public async Task<IReadOnlyList<CollectionBulkTarget>> GetRevisionImpactTargetsAsync(
        CollectionDefinitionId definition, int revision,
        IEnumerable<INamedRevisionImpactCondition> namedConditions,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var row = await db.RevisionImpacts.AsNoTracking().SingleAsync(x =>
            x.DefinitionId == definition.Value && x.Revision == revision, cancellationToken).ConfigureAwait(false);
        var impact = new RevisionImpact(row.ScopeType, row.ScopePayload);
        var conditions = namedConditions.ToDictionary(x => x.Name, StringComparer.Ordinal);
        ValidateImpact(impact, conditions.Values);
        var candidates = await LoadRevisionCandidatesAsync(db, definition.Value, cancellationToken).ConfigureAwait(false);
        return candidates.Where(x => MatchesImpact(x, impact, conditions))
            .Select(x => new CollectionBulkTarget(x.Resource, x.EffectiveDate, x.Attributes)).ToList();
    }

    public async Task<long> UpsertLocationAsync(ResourceKey resource, CollectionDefinitionId definition, Uri url,
        ResourceLocationSource source, DateTimeOffset discoveredAt, CancellationToken cancellationToken = default)
    {
        resource = resource.Normalize();
        CollectionHttpUrl.EnsureHttp(url, nameof(url));
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
                    ResourcePk = item.ResourcePk,
                    DefinitionId = definition.Value,
                    Url = url.AbsoluteUri,
                    Source = source,
                    Status = ResourceLocationStatus.Unknown,
                    DiscoveredAt = discoveredAt,
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
            .Select(x => (Location: x, Valid: CollectionHttpUrl.TryCreate(x.Url, out var url), Url: url))
            .Where(x => x.Valid)
            .Select(x => new ResourceLocationCandidate(x.Location.LocationId, x.Url!, x.Location.Source,
                x.Location.Status, x.Location.LastVerifiedAt))
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
            ApplyLocationOutcome(location, result, now, errorCode);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private static void ApplyLocationOutcome(ResourceLocationEntity location, CollectionAttemptResult result,
        DateTimeOffset now, string? errorCode)
    {
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
    }

    public async Task<IReadOnlyList<PendingCollectionDispatch>> GetPendingDispatchesAsync(DateTimeOffset now, int maxCount,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        if (await db.Controls.AsNoTracking().AnyAsync(x => x.ControlId == "pipeline" && x.IsPaused, cancellationToken))
            return [];
        var pending = await (from outbox in db.DispatchOutbox.AsNoTracking()
                             join task in db.Tasks.AsNoTracking() on outbox.TaskId equals task.TaskId
                             join resource in db.Resources.AsNoTracking() on task.ResourcePk equals resource.ResourcePk
                             where outbox.DispatchedAt == null
                                   && (outbox.ReservedUntilUnixMilliseconds == null
                                       || outbox.ReservedUntilUnixMilliseconds <= now.ToUnixTimeMilliseconds())
                             select new { outbox, task, resource }).ToListAsync(cancellationToken);
        return pending.Where(x => x.outbox.AvailableAt <= now).OrderBy(x => x.outbox.AvailableAt)
            .Take(Math.Max(1, maxCount))
            .Select(x => new PendingCollectionDispatch(x.outbox.OutboxId,
                new CollectionTaskNotification(x.outbox.TaskId, x.outbox.DispatchGeneration),
                new ResourceKey(x.resource.Type, x.resource.Provider, x.resource.ResourceId),
                new CollectionDefinitionId(x.task.DefinitionId), x.resource.EffectiveDate,
                x.task.Lane, x.task.Priority, x.outbox.AvailableAt, x.outbox.CreatedAt,
                JsonSerializer.Deserialize<Dictionary<string, string>>(x.resource.AttributesJson) ?? [])).ToList();
    }

    public async Task<CollectionLaneDispatchState> GetLaneDispatchStateAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var rows = await (from outbox in db.DispatchOutbox.AsNoTracking()
                          join task in db.Tasks.AsNoTracking() on outbox.TaskId equals task.TaskId
                          where outbox.DispatchedAt != null && outbox.EnvelopeId != null
                          select new { outbox.EnvelopeId, outbox.DispatchedAt, task.Lane })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var envelopes = rows.GroupBy(x => x.EnvelopeId!.Value)
            .Select(x => new { DispatchedAt = x.Max(y => y.DispatchedAt)!.Value, Lane = x.First().Lane })
            .OrderByDescending(x => x.DispatchedAt);
        var consecutiveRealtime = 0;
        CollectionLane? lastNonRealtimeLane = null;
        foreach (var envelope in envelopes)
        {
            if (lastNonRealtimeLane is null && envelope.Lane != CollectionLane.Realtime)
                lastNonRealtimeLane = envelope.Lane;
            if (envelope.Lane == CollectionLane.Realtime && lastNonRealtimeLane is null)
                consecutiveRealtime++;
            if (lastNonRealtimeLane is not null) break;
        }
        return new CollectionLaneDispatchState(consecutiveRealtime, lastNonRealtimeLane);
    }

    public async Task<bool> TryReserveDispatchesWithinCapacityAsync(IReadOnlyCollection<Guid> outboxIds,
        string reservationToken, Guid envelopeId, DateTimeOffset now, TimeSpan duration, int maxInFlightEnvelopes,
        CancellationToken cancellationToken = default)
    {
        var ids = outboxIds.Distinct().ToArray();
        if (ids.Length == 0 || string.IsNullOrWhiteSpace(reservationToken) || envelopeId == Guid.Empty) return false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (await db.Controls.AnyAsync(x => x.ControlId == "pipeline" && x.IsPaused, cancellationToken))
                return false;

            var inFlight = await db.ExecutionLeases.CountAsync(x =>
                (x.Status == "StartPending" || x.Status == "Running") && x.LeaseExpiresAt > now,
                cancellationToken).ConfigureAwait(false);
            var executionEnvelopeIds = db.ExecutionLeases.Select(x => x.DispatchEnvelopeId);
            var legacyInFlight = await (from outbox in db.DispatchOutbox
                                        join task in db.Tasks on outbox.TaskId equals task.TaskId
                                        where outbox.EnvelopeId != null && outbox.DispatchedAt != null
                                              && outbox.DispatchGeneration == task.DispatchGeneration
                                              && (task.Status == CollectionTaskStatus.Ready
                                                  || task.Status == CollectionTaskStatus.Running)
                                              && !executionEnvelopeIds.Contains(outbox.EnvelopeId.Value)
                                        select outbox.EnvelopeId).Distinct().CountAsync(cancellationToken)
                .ConfigureAwait(false);
            var reserved = await db.DispatchOutbox
                .Where(x => x.DispatchedAt == null && x.EnvelopeId != null
                            && x.ReservedUntilUnixMilliseconds > now.ToUnixTimeMilliseconds())
                .Select(x => x.EnvelopeId).Distinct().CountAsync(cancellationToken).ConfigureAwait(false);
            if (inFlight + legacyInFlight + reserved >= Math.Max(1, maxInFlightEnvelopes)) return false;

            var rows = await db.DispatchOutbox.Where(x => ids.Contains(x.OutboxId) && x.DispatchedAt == null
                    && (x.ReservedUntilUnixMilliseconds == null
                        || x.ReservedUntilUnixMilliseconds <= now.ToUnixTimeMilliseconds()))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (rows.Count != ids.Length) return false;
            foreach (var row in rows)
            {
                row.ReservationToken = reservationToken;
                row.ReservedUntilUnixMilliseconds = now.Add(duration).ToUnixTimeMilliseconds();
                row.EnvelopeId = envelopeId;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task MarkWakeSentAsync(Guid envelopeId, string reservationToken, string? queueMessageId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var rows = await db.DispatchOutbox.Where(x => x.EnvelopeId == envelopeId
                    && x.ReservationToken == reservationToken && x.DispatchedAt == null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows) row.QueueMessageId = queueMessageId;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<int> ReclaimExpiredExecutionLeasesAsync(DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var tx = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
            db.Database.UseTransaction(tx);
            var before = await db.ExecutionLeases.CountAsync(x => (x.Status == "StartPending" || x.Status == "Running")
                && x.LeaseExpiresAt <= now, cancellationToken).ConfigureAwait(false);
            await ReclaimExpiredExecutionLeasesAsync(db, now, cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return before;
        }
        finally { _gate.Release(); }
    }

    public async Task<CollectionExecutionAcquireResult> AcquireNextExecutionAsync(CollectionWakeSignal wake,
        string queueMessageId, DateTimeOffset now, TimeSpan startLease,
        CancellationToken cancellationToken = default)
    {
        if (wake.ContractVersion != 1 || wake.WakeId == Guid.Empty || wake.DispatchEnvelopeId == Guid.Empty
            || string.IsNullOrWhiteSpace(wake.ReservationToken))
            return new(CollectionExecutionAcquireStatus.NoWork);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var tx = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
            db.Database.UseTransaction(tx);
            await ReclaimExpiredExecutionLeasesAsync(db, now, cancellationToken).ConfigureAwait(false);
            if (await db.Controls.AnyAsync(x => x.ControlId == "pipeline" && x.IsPaused, cancellationToken))
                return new(CollectionExecutionAcquireStatus.NoWork);

            var existing = await db.ExecutionLeases.SingleOrDefaultAsync(
                x => x.DispatchEnvelopeId == wake.DispatchEnvelopeId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                var priorEnvelope = existing.Status == "StartPending" && existing.LeaseExpiresAt > now
                    && existing.WakeId == wake.WakeId && existing.ReservationToken == wake.ReservationToken
                    ? await BuildExecutionEnvelopeAsync(db, wake.DispatchEnvelopeId, cancellationToken).ConfigureAwait(false)
                    : null;
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return priorEnvelope is null ? new(CollectionExecutionAcquireStatus.NoWork)
                    : new(CollectionExecutionAcquireStatus.Acquired, existing.ExecutionBatchId,
                        existing.LeaseToken, priorEnvelope, existing.LeaseExpiresAt);
            }

            var rows = await db.DispatchOutbox.Where(x => x.EnvelopeId == wake.DispatchEnvelopeId
                    && x.ReservationToken == wake.ReservationToken && x.DispatchedAt == null
                    && x.ReservedUntilUnixMilliseconds > now.ToUnixTimeMilliseconds())
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0) return new(CollectionExecutionAcquireStatus.NoWork);
            var taskIds = rows.Select(x => x.TaskId).ToArray();
            var tasks = await db.Tasks.Where(x => taskIds.Contains(x.TaskId)).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (tasks.Count != rows.Count || rows.Any(row => tasks.All(task => task.TaskId != row.TaskId
                    || task.DispatchGeneration != row.DispatchGeneration || task.Status != CollectionTaskStatus.Ready)))
                return new(CollectionExecutionAcquireStatus.NoWork);

            var lease = new CollectionExecutionLeaseEntity
            {
                ExecutionBatchId = Guid.NewGuid(),
                DispatchEnvelopeId = wake.DispatchEnvelopeId,
                WakeId = wake.WakeId,
                ReservationToken = wake.ReservationToken,
                LeaseToken = Guid.NewGuid().ToString("N"),
                Status = "StartPending",
                LeaseExpiresAt = now.AddSeconds(Math.Clamp((int)startLease.TotalSeconds, 10, 60)),
                CreatedAt = now,
                QueueMessageId = queueMessageId,
            };
            db.ExecutionLeases.Add(lease);
            foreach (var row in rows)
            {
                row.DispatchedAt = now;
                row.QueueMessageId = queueMessageId;
                row.ReservationToken = null;
                row.ReservedUntilUnixMilliseconds = null;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var envelope = await BuildExecutionEnvelopeAsync(db, wake.DispatchEnvelopeId, cancellationToken)
                .ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return envelope is null ? new(CollectionExecutionAcquireStatus.NoWork)
                : new(CollectionExecutionAcquireStatus.Acquired, lease.ExecutionBatchId, lease.LeaseToken,
                    envelope, lease.LeaseExpiresAt);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> StartExecutionAsync(Guid executionBatchId, CollectionExecutionStartRequest request,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var lease = await db.ExecutionLeases.SingleOrDefaultAsync(x => x.ExecutionBatchId == executionBatchId,
                cancellationToken).ConfigureAwait(false);
            if (lease is null || lease.LeaseToken != request.LeaseToken || lease.LeaseExpiresAt <= now) return false;
            if (lease.Status == "Running") return true;
            if (lease.Status != "StartPending") return false;
            lease.Status = "Running";
            lease.StartedAt = now;
            lease.LambdaRequestId = request.LambdaRequestId;
            lease.LeaseExpiresAt = now.AddSeconds(Math.Clamp(request.LeaseSeconds, 60, 1200));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> CompleteExecutionAsync(Guid executionBatchId, string leaseToken,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var lease = await db.ExecutionLeases.SingleOrDefaultAsync(x => x.ExecutionBatchId == executionBatchId,
                cancellationToken).ConfigureAwait(false);
            if (lease is null || lease.LeaseToken != leaseToken) return false;
            if (lease.Status == "Completed") return true;
            if (lease.Status is not ("Running" or "StartPending")) return false;
            await ReleaseUnstartedDispatchesAsync(db, lease.DispatchEnvelopeId, cancellationToken).ConfigureAwait(false);
            lease.Status = "Completed";
            lease.FinishedAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> TryReserveDispatchesAsync(IReadOnlyCollection<Guid> outboxIds, string reservationToken,
        Guid envelopeId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var ids = outboxIds.Distinct().ToArray();
        if (ids.Length == 0 || string.IsNullOrWhiteSpace(reservationToken) || envelopeId == Guid.Empty) return false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var rows = await db.DispatchOutbox.Where(x => ids.Contains(x.OutboxId) && x.DispatchedAt == null
                    && (x.ReservedUntilUnixMilliseconds == null
                        || x.ReservedUntilUnixMilliseconds <= now.ToUnixTimeMilliseconds()))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (rows.Count != ids.Length) return false;
            foreach (var row in rows)
            {
                row.ReservationToken = reservationToken;
                row.ReservedUntilUnixMilliseconds = now.Add(duration).ToUnixTimeMilliseconds();
                row.EnvelopeId = envelopeId;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> MarkDispatchedAsync(IReadOnlyCollection<Guid> outboxIds, string reservationToken,
        Guid envelopeId, string? queueMessageId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var ids = outboxIds.Distinct().ToArray();
        if (ids.Length == 0) return false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var rows = await db.DispatchOutbox.Where(x => ids.Contains(x.OutboxId) && x.DispatchedAt == null
                    && x.ReservationToken == reservationToken && x.EnvelopeId == envelopeId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (rows.Count != ids.Length) return false;
            foreach (var row in rows)
            {
                row.DispatchedAt = now;
                row.QueueMessageId = queueMessageId;
                row.ReservationToken = null;
                row.ReservedUntilUnixMilliseconds = null;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
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

    public async Task<CollectionTaskPage> SearchTasksAsync(CollectionTaskQuery request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        await using var db = CreateDbContext();
        var query = from task in db.Tasks.AsNoTracking()
                    join resource in db.Resources.AsNoTracking() on task.ResourcePk equals resource.ResourcePk
                    select new { task, resource };

        if (request.LatestOnly)
            return await SearchLatestTasksAsync(db, request, page, pageSize, cancellationToken)
                .ConfigureAwait(false);

        if (request.Statuses is { Count: > 0 })
        {
            var statuses = request.Statuses.Distinct().ToArray();
            query = query.Where(x => statuses.Contains(x.task.Status));
        }
        if (request.ActionableOnly)
            query = query.Where(x => db.FailureNotifications.Any(notification =>
                notification.TaskId == x.task.TaskId
                && notification.ResolutionStatus == CollectionFailureResolutionStatus.Open));
        if (request.ResourceType.HasValue)
            query = query.Where(x => x.resource.Type == request.ResourceType.Value);
        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            var provider = request.Provider.Trim().ToUpperInvariant();
            query = query.Where(x => x.resource.Provider == provider);
        }
        if (!string.IsNullOrWhiteSpace(request.DefinitionId))
        {
            var definition = request.DefinitionId.Trim();
            query = query.Where(x => x.task.DefinitionId == definition);
        }
        if (request.Lane.HasValue)
            query = query.Where(x => x.task.Lane == request.Lane.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(x => x.resource.ResourceId.Contains(search)
                                     || x.resource.Provider.Contains(search)
                                     || x.task.DefinitionId.Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(request.ErrorSearch))
        {
            var error = request.ErrorSearch.Trim();
            query = query.Where(x => db.Attempts.Any(a => a.TaskId == x.task.TaskId
                && ((a.ErrorCode != null && a.ErrorCode.Contains(error))
                    || (a.ErrorMessage != null && a.ErrorMessage.Contains(error)))));
        }
        if (request.CreatedFrom.HasValue || request.CreatedTo.HasValue)
        {
            // The SQLite provider cannot translate DateTimeOffset comparisons. Apply only this optional
            // administration filter in memory after all selective SQL predicates have run.
            var candidates = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
            var filtered = candidates.Where(x => (!request.CreatedFrom.HasValue
                                                   || x.task.CreatedAt >= request.CreatedFrom.Value)
                                                  && (!request.CreatedTo.HasValue
                                                      || x.task.CreatedAt <= request.CreatedTo.Value))
                .OrderBy(x => x.task.Status == CollectionTaskStatus.Succeeded)
                .ThenBy(x => x.task.Lane switch
                {
                    CollectionLane.Realtime => 0,
                    CollectionLane.Normal => 1,
                    CollectionLane.Background => 2,
                    _ => 3,
                })
                .ThenByDescending(x => x.task.Priority)
                .ThenBy(x => x.task.DefinitionId, StringComparer.Ordinal)
                .ThenBy(x => x.task.TaskId)
                .ToList();
            return new(filtered.Count, page, pageSize, filtered.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new CollectionTaskSummary(x.task.TaskId,
                    new ResourceKey(x.resource.Type, x.resource.Provider, x.resource.ResourceId),
                    new CollectionDefinitionId(x.task.DefinitionId), x.task.Status, x.task.Lane, x.task.Priority,
                    x.task.RequestedRevision, x.task.AvailableAt, x.task.AttemptCount)).ToList());
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        // Keep actionable work ahead of completed history, then mirror the stable part of the
        // dispatcher precedence. Aging and starvation prevention are intentionally runtime-only.
        var rows = await query
            .OrderBy(x => x.task.Status == CollectionTaskStatus.Succeeded)
            .ThenBy(x => x.task.Lane == CollectionLane.Realtime ? 0
                : x.task.Lane == CollectionLane.Normal ? 1
                : x.task.Lane == CollectionLane.Background ? 2 : 3)
            .ThenByDescending(x => x.task.Priority)
            .ThenBy(x => x.task.DefinitionId)
            .ThenBy(x => x.task.TaskId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Select(x => new CollectionTaskSummary(x.task.TaskId,
            new ResourceKey(x.resource.Type, x.resource.Provider, x.resource.ResourceId),
            new CollectionDefinitionId(x.task.DefinitionId), x.task.Status, x.task.Lane, x.task.Priority,
            x.task.RequestedRevision, x.task.AvailableAt, x.task.AttemptCount)).ToList();
        return new(totalCount, page, pageSize, items);
    }

    private static async Task<CollectionTaskPage> SearchLatestTasksAsync(CollectionPlatformDbContext db,
        CollectionTaskQuery request, int page, int pageSize, CancellationToken cancellationToken)
    {
        var allRows = await (from task in db.Tasks.AsNoTracking()
                             join resource in db.Resources.AsNoTracking()
                                 on task.ResourcePk equals resource.ResourcePk
                             select new TaskSearchRow(task, resource))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        IEnumerable<TaskSearchRow> filtered = allRows
            .GroupBy(x => new { x.Task.ResourcePk, x.Task.DefinitionId })
            .Select(group => group.OrderByDescending(x => x.Task.CreatedAt)
                .ThenByDescending(x => x.Task.TaskId).First());

        if (request.Statuses is { Count: > 0 })
        {
            var statuses = request.Statuses.Distinct().ToHashSet();
            filtered = filtered.Where(x => statuses.Contains(x.Task.Status));
        }
        if (request.ActionableOnly)
        {
            var actionableTaskIds = (await db.FailureNotifications.AsNoTracking()
                    .Where(x => x.ResolutionStatus == CollectionFailureResolutionStatus.Open)
                    .Select(x => x.TaskId).ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToHashSet();
            filtered = filtered.Where(x => actionableTaskIds.Contains(x.Task.TaskId));
        }
        if (request.ResourceType.HasValue)
            filtered = filtered.Where(x => x.Resource.Type == request.ResourceType.Value);
        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            var provider = request.Provider.Trim().ToUpperInvariant();
            filtered = filtered.Where(x => x.Resource.Provider == provider);
        }
        if (!string.IsNullOrWhiteSpace(request.DefinitionId))
        {
            var definition = request.DefinitionId.Trim();
            filtered = filtered.Where(x => x.Task.DefinitionId == definition);
        }
        if (request.Lane.HasValue)
            filtered = filtered.Where(x => x.Task.Lane == request.Lane.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            filtered = filtered.Where(x => x.Resource.ResourceId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.Resource.Provider.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.Task.DefinitionId.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(request.ErrorSearch))
        {
            var error = request.ErrorSearch.Trim();
            var matchingTaskIds = (await db.Attempts.AsNoTracking()
                    .Where(x => (x.ErrorCode != null && x.ErrorCode.Contains(error))
                        || (x.ErrorMessage != null && x.ErrorMessage.Contains(error)))
                    .Select(x => x.TaskId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToHashSet();
            filtered = filtered.Where(x => matchingTaskIds.Contains(x.Task.TaskId));
        }
        if (request.CreatedFrom.HasValue)
            filtered = filtered.Where(x => x.Task.CreatedAt >= request.CreatedFrom.Value);
        if (request.CreatedTo.HasValue)
            filtered = filtered.Where(x => x.Task.CreatedAt <= request.CreatedTo.Value);

        var ordered = filtered
            .OrderBy(x => x.Task.Status == CollectionTaskStatus.Succeeded)
            .ThenBy(x => x.Task.Lane switch
            {
                CollectionLane.Realtime => 0,
                CollectionLane.Normal => 1,
                CollectionLane.Background => 2,
                _ => 3,
            })
            .ThenByDescending(x => x.Task.Priority)
            .ThenBy(x => x.Task.DefinitionId, StringComparer.Ordinal)
            .ThenBy(x => x.Task.TaskId)
            .ToList();
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new CollectionTaskSummary(x.Task.TaskId,
                new ResourceKey(x.Resource.Type, x.Resource.Provider, x.Resource.ResourceId),
                new CollectionDefinitionId(x.Task.DefinitionId), x.Task.Status, x.Task.Lane, x.Task.Priority,
                x.Task.RequestedRevision, x.Task.AvailableAt, x.Task.AttemptCount)).ToList();
        return new(ordered.Count, page, pageSize, items);
    }

    private sealed record TaskSearchRow(CollectionTaskEntity Task, CollectionResourceEntity Resource);

    public async Task<CollectionTaskViewCounts> GetTaskViewCountsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var tasks = await db.Tasks.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var latestTasks = tasks.GroupBy(x => new { x.ResourcePk, x.DefinitionId })
            .Select(group => group.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.TaskId).First())
            .ToList();
        var counts = latestTasks.GroupBy(x => x.Status).ToDictionary(x => x.Key, x => x.Count());
        int Count(params CollectionTaskStatus[] statuses) => statuses.Sum(x => counts.GetValueOrDefault(x));
        var openFailureTaskIds = (await db.FailureNotifications.AsNoTracking()
                .Where(x => x.ResolutionStatus == CollectionFailureResolutionStatus.Open)
                .Select(x => x.TaskId).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet();
        var actionableCount = latestTasks.Count(task =>
            task.Status is CollectionTaskStatus.Failed or CollectionTaskStatus.DeadLetter
            && openFailureTaskIds.Contains(task.TaskId));
        return new(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["attention"] = actionableCount,
            ["running"] = Count(CollectionTaskStatus.Running),
            ["waiting"] = Count(CollectionTaskStatus.Pending, CollectionTaskStatus.Ready,
                CollectionTaskStatus.RetryWaiting, CollectionTaskStatus.WaitingDiscovery),
            ["recent"] = Count(CollectionTaskStatus.Succeeded),
            ["all"] = counts.Values.Sum(),
        });
    }

    public async Task<CollectionStatePage> SearchStatesAsync(CollectionStateQuery request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        await using var db = CreateDbContext();
        var query = from state in db.States.AsNoTracking()
                    join resource in db.Resources.AsNoTracking() on state.ResourcePk equals resource.ResourcePk
                    select new { state, resource };
        if (request.Statuses is { Count: > 0 })
        {
            var statuses = request.Statuses.Distinct().ToArray();
            query = query.Where(x => statuses.Contains(x.state.Status));
        }
        if (request.ResourceType.HasValue) query = query.Where(x => x.resource.Type == request.ResourceType.Value);
        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            var provider = request.Provider.Trim().ToUpperInvariant();
            query = query.Where(x => x.resource.Provider == provider);
        }
        if (!string.IsNullOrWhiteSpace(request.DefinitionId))
        {
            var definition = request.DefinitionId.Trim();
            query = query.Where(x => x.state.DefinitionId == definition);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(x => x.resource.ResourceId.Contains(search)
                || x.resource.Provider.Contains(search) || x.state.DefinitionId.Contains(search));
        }
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderBy(x => x.resource.Type).ThenBy(x => x.resource.ResourceId)
            .ThenBy(x => x.state.DefinitionId).Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new(total, page, pageSize, rows.Select(x => new CollectionStateSnapshot(
            new(x.resource.Type, x.resource.Provider, x.resource.ResourceId), new(x.state.DefinitionId),
            x.state.AppliedRevision, x.state.RequiredRevision, x.state.LastCollectedAt,
            x.state.NextCollectionAt, x.state.Status)).ToList());
    }

    public async Task<CollectionReadinessSnapshot> GetReadinessAsync(string requestedByRaceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestedByRaceId))
            throw new ArgumentException("Race id is required.", nameof(requestedByRaceId));
        await using var db = CreateDbContext();
        var active = await (from guard in db.ActiveTasks.AsNoTracking()
                            join task in db.Tasks.AsNoTracking() on guard.TaskId equals task.TaskId
                            join resource in db.Resources.AsNoTracking() on guard.ResourcePk equals resource.ResourcePk
                            select new { task.DefinitionId, resource.AttributesJson }).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var definitions = active.Where(x =>
        {
            var attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(x.AttributesJson) ?? [];
            return attributes.TryGetValue("requestedByRaceId", out var value)
                && string.Equals(value, requestedByRaceId, StringComparison.Ordinal);
        }).Select(x => x.DefinitionId).ToArray();
        return new(definitions.Count(x => x == "horse-profile"),
            definitions.Count(x => x == "jockey-profile"),
            definitions.Count(x => x == "race-result"),
            definitions.Count(x => x == "trainer-profile"));
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

    public async Task<CollectionPipelineState> GetPipelineStateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var control = await db.Controls.AsNoTracking().SingleOrDefaultAsync(x => x.ControlId == "pipeline", cancellationToken);
        return control is null ? new(false, null, null) : new(control.IsPaused, control.Reason, control.UpdatedAt);
    }

    public async Task SetPausedAsync(bool paused, string? reason, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var control = await db.Controls.SingleOrDefaultAsync(x => x.ControlId == "pipeline", cancellationToken);
            if (control is null)
            {
                control = new CollectionPlatformControlEntity { ControlId = "pipeline" };
                db.Controls.Add(control);
            }
            control.IsPaused = paused;
            control.Reason = paused ? reason : null;
            control.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> CancelTaskAsync(Guid taskId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var task = await db.Tasks.SingleOrDefaultAsync(x => x.TaskId == taskId, cancellationToken);
            if (task is null || task.Status is CollectionTaskStatus.Succeeded or CollectionTaskStatus.Failed
                or CollectionTaskStatus.Cancelled or CollectionTaskStatus.DeadLetter) return false;
            task.CancellationRequestedAt = now;
            task.UpdatedAt = now;
            if (task.Status != CollectionTaskStatus.Running)
            {
                task.Status = CollectionTaskStatus.Cancelled;
                task.FinishedAt = now;
                var active = await db.ActiveTasks.SingleOrDefaultAsync(x => x.TaskId == taskId, cancellationToken);
                if (active is not null) db.ActiveTasks.Remove(active);
                var state = await db.States.SingleAsync(x => x.ResourcePk == task.ResourcePk
                    && x.DefinitionId == task.DefinitionId, cancellationToken);
                state.Status = CollectionStateStatus.Unknown;
                state.UpdatedAt = now;
                if (await ReopenFailuresAsync(db, taskId, cancellationToken).ConfigureAwait(false) > 0)
                    state.Status = CollectionStateStatus.Failed;
                var pending = await db.DispatchOutbox.Where(x => x.TaskId == taskId && x.DispatchedAt == null)
                    .ToListAsync(cancellationToken);
                foreach (var item in pending) item.DispatchedAt = now;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<LegacyRaceDetailMergeReport> MergeLegacyRaceDetailsAsync(bool execute,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var legacyTypes = new[] { ResourceType.RaceCard, ResourceType.RaceResult };
            var legacy = await db.Resources.Where(x => legacyTypes.Contains(x.Type)).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var errors = new List<string>();
            var mapped = new List<(CollectionResourceEntity Source, string Id, DateOnly Date,
                Dictionary<string, string> Attributes)>();
            foreach (var source in legacy)
            {
                Dictionary<string, string> attributes;
                try
                {
                    attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(source.AttributesJson) ?? [];
                }
                catch (JsonException)
                {
                    errors.Add($"Invalid attributes JSON: {source.Type}/{source.Provider}/{source.ResourceId}");
                    continue;
                }
                if (source.EffectiveDate is not { } date
                    || !attributes.TryGetValue("course", out var course)
                    || !attributes.TryGetValue("number", out var numberText)
                    || !int.TryParse(numberText, out var number) || number is < 1 or > 12
                    || !TryCanonicalCourse(course, out var canonicalCourse))
                {
                    errors.Add($"Unidentified race resource: {source.Type}/{source.Provider}/{source.ResourceId}");
                    continue;
                }
                mapped.Add((source, $"{date:yyyyMMdd}:{canonicalCourse}:{number}", date, attributes));
            }
            var legacyPks = legacy.Select(x => x.ResourcePk).ToArray();
            var raceDetailDefinition = await db.Definitions.AsNoTracking().SingleOrDefaultAsync(
                x => x.DefinitionId == "race-detail", cancellationToken).ConfigureAwait(false);
            if (raceDetailDefinition is null || !raceDetailDefinition.Enabled
                || raceDetailDefinition.ResourceType != ResourceType.Race || raceDetailDefinition.CurrentRevision < 1)
                errors.Add("The enabled race-detail revision 1 definition must be registered before migration.");
            var activeCount = await db.ActiveTasks.CountAsync(x => legacyPks.Contains(x.ResourcePk), cancellationToken)
                .ConfigureAwait(false);
            if (!execute && activeCount > 0)
                errors.Add($"Legacy race collection has {activeCount} active task(s); apply will cancel them.");
            var targetIds = mapped.Select(x => x.Id).Distinct().ToArray();
            var activeTargetCount = await db.ActiveTasks.Join(db.Resources.Where(x => x.Type == ResourceType.Race
                        && targetIds.Contains(x.ResourceId)), active => active.ResourcePk, resource => resource.ResourcePk,
                    (active, _) => active)
                .CountAsync(x => x.DefinitionId == "race-detail", cancellationToken).ConfigureAwait(false);
            if (!execute && activeTargetCount > 0)
                errors.Add($"Unified race collection has {activeTargetCount} active task(s); apply will cancel them.");
            if (errors.Count > 0 || !execute)
            {
                var previewGroups = mapped.Select(x => (x.Source.Provider, x.Id)).Distinct().Count();
                var previewRequests = await db.Requests.CountAsync(x => legacyPks.Contains(x.ResourcePk), cancellationToken);
                var previewTasks = await db.Tasks.CountAsync(x => legacyPks.Contains(x.ResourcePk), cancellationToken);
                var previewTaskIds = await db.Tasks.Where(x => legacyPks.Contains(x.ResourcePk))
                    .Select(x => x.TaskId).ToArrayAsync(cancellationToken).ConfigureAwait(false);
                var previewAttempts = await db.Attempts.CountAsync(x => previewTaskIds.Contains(x.TaskId), cancellationToken);
                var previewLocations = await db.Locations.CountAsync(x => legacyPks.Contains(x.ResourcePk), cancellationToken);
                var previewStates = await db.States.CountAsync(x => legacyPks.Contains(x.ResourcePk), cancellationToken);
                var previewToday = DateOnly.FromDateTime(HorseRacingPrediction.Contracts.Time.JstTime.Convert(now).Date);
                var previewSupplements = mapped.GroupBy(x => (x.Source.Provider, x.Id)).Count(group =>
                {
                    var pks = group.Select(x => x.Source.ResourcePk).ToArray();
                    var groupStates = db.States.AsNoTracking().Where(x => pks.Contains(x.ResourcePk)).ToList();
                    var cardCurrent = groupStates.Any(x => x.DefinitionId == "race-card" && x.Status == CollectionStateStatus.Current);
                    var resultCurrent = groupStates.Any(x => x.DefinitionId == "race-result" && x.Status == CollectionStateStatus.Current);
                    var date = group.First().Date;
                    return date > previewToday || (date >= previewToday.AddDays(-5)
                        ? !(cardCurrent && resultCurrent) : !resultCurrent);
                });
                return new(!execute, legacy.Count, previewGroups, previewRequests, previewTasks, previewAttempts,
                    previewLocations, previewStates, previewSupplements, errors);
            }

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var activeRaceTasks = await (from active in db.ActiveTasks
                                         join task in db.Tasks on active.TaskId equals task.TaskId
                                         join resource in db.Resources on active.ResourcePk equals resource.ResourcePk
                                         where legacyPks.Contains(resource.ResourcePk)
                                             || (resource.Type == ResourceType.Race
                                                 && targetIds.Contains(resource.ResourceId)
                                                 && active.DefinitionId == "race-detail")
                                         select new { active, task }).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var item in activeRaceTasks)
            {
                item.task.CancellationRequestedAt = now;
                item.task.Status = CollectionTaskStatus.Cancelled;
                item.task.FinishedAt = now;
                item.task.UpdatedAt = now;
                db.ActiveTasks.Remove(item.active);
                var pending = await db.DispatchOutbox.Where(x => x.TaskId == item.task.TaskId && x.DispatchedAt == null)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var outbox in pending) outbox.DispatchedAt = now;
            }
            var requestCount = 0;
            var taskCount = 0;
            var attemptCount = 0;
            var locationCount = 0;
            var stateCount = 0;
            var supplementCount = 0;
            var today = DateOnly.FromDateTime(HorseRacingPrediction.Contracts.Time.JstTime.Convert(now).Date);
            foreach (var group in mapped.GroupBy(x => (x.Source.Provider, x.Id)))
            {
                var first = group.First();
                var target = await db.Resources.SingleOrDefaultAsync(x => x.Type == ResourceType.Race
                    && x.Provider == group.Key.Provider && x.ResourceId == group.Key.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (target is null)
                {
                    target = new CollectionResourceEntity
                    {
                        Type = ResourceType.Race,
                        Provider = group.Key.Provider,
                        ResourceId = group.Key.Id,
                        EffectiveDate = first.Date,
                        AttributesJson = JsonSerializer.Serialize(MergeAttributes(group.Select(x => x.Attributes))),
                        CreatedAt = group.Min(x => x.Source.CreatedAt),
                    };
                    db.Resources.Add(target);
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                var sourcePks = group.Select(x => x.Source.ResourcePk).ToArray();
                var requests = await db.Requests.Where(x => sourcePks.Contains(x.ResourcePk)
                    && (x.DefinitionId == "race-card" || x.DefinitionId == "race-result"))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var request in requests)
                {
                    request.OriginDefinitionId ??= request.DefinitionId;
                    request.OriginRequestedRevision ??= request.RequestedRevision;
                    request.ResourcePk = target.ResourcePk;
                    request.DefinitionId = "race-detail";
                    request.RequestedRevision = 1;
                }
                requestCount += requests.Count;
                var tasks = await db.Tasks.Where(x => sourcePks.Contains(x.ResourcePk)
                    && (x.DefinitionId == "race-card" || x.DefinitionId == "race-result"))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var task in tasks)
                {
                    task.OriginDefinitionId ??= task.DefinitionId;
                    task.OriginRequestedRevision ??= task.RequestedRevision;
                    task.ResourcePk = target.ResourcePk;
                    task.DefinitionId = "race-detail";
                    task.RequestedRevision = 1;
                }
                taskCount += tasks.Count;
                var taskIds = tasks.Select(x => x.TaskId).ToArray();
                attemptCount += await db.Attempts.CountAsync(x => taskIds.Contains(x.TaskId), cancellationToken)
                    .ConfigureAwait(false);

                var locations = await db.Locations.Where(x => sourcePks.Contains(x.ResourcePk)
                    && (x.DefinitionId == "race-card" || x.DefinitionId == "race-result"))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var location in locations)
                {
                    var duplicate = await db.Locations.FirstOrDefaultAsync(x => x.ResourcePk == target.ResourcePk
                        && x.DefinitionId == "race-detail" && x.Url == location.Url, cancellationToken)
                        .ConfigureAwait(false);
                    if (duplicate is null)
                    {
                        location.ResourcePk = target.ResourcePk;
                        location.DefinitionId = "race-detail";
                    }
                    else
                    {
                        MergeLocation(duplicate, location);
                        db.Locations.Remove(location);
                    }
                }
                locationCount += locations.Count;

                var states = await db.States.Where(x => sourcePks.Contains(x.ResourcePk)
                    && (x.DefinitionId == "race-card" || x.DefinitionId == "race-result"))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                var cardCurrent = states.Any(x => x.DefinitionId == "race-card" && x.Status == CollectionStateStatus.Current);
                var resultCurrent = states.Any(x => x.DefinitionId == "race-result" && x.Status == CollectionStateStatus.Current);
                var requiresCard = first.Date >= today.AddDays(-5);
                var complete = first.Date > today ? false : requiresCard ? cardCurrent && resultCurrent : resultCurrent;
                var targetState = await db.States.SingleOrDefaultAsync(x => x.ResourcePk == target.ResourcePk
                    && x.DefinitionId == "race-detail", cancellationToken).ConfigureAwait(false);
                targetState ??= new CollectionStateEntity { ResourcePk = target.ResourcePk, DefinitionId = "race-detail" };
                if (db.Entry(targetState).State == EntityState.Detached) db.States.Add(targetState);
                targetState.RequiredRevision = 1;
                targetState.AppliedRevision = complete ? 1 : 0;
                targetState.Status = complete ? CollectionStateStatus.Current : CollectionStateStatus.Pending;
                targetState.LastCollectedAt = states.Select(x => x.LastCollectedAt).DefaultIfEmpty().Max();
                targetState.NextCollectionAt = complete ? null : now;
                targetState.UpdatedAt = now;
                db.States.RemoveRange(states);
                stateCount += states.Count;
                if (!complete)
                {
                    var request = new CollectionRequestEntity
                    {
                        RequestId = Guid.NewGuid(),
                        ResourcePk = target.ResourcePk,
                        DefinitionId = "race-detail",
                        RequestedRevision = 1,
                        Reason = CollectionReason.DefinitionChanged,
                        RequestedAt = now,
                    };
                    var task = new CollectionTaskEntity
                    {
                        TaskId = Guid.NewGuid(),
                        RequestId = request.RequestId,
                        ResourcePk = target.ResourcePk,
                        DefinitionId = "race-detail",
                        RequestedRevision = 1,
                        Status = CollectionTaskStatus.Ready,
                        Lane = CollectionLane.Realtime,
                        Priority = (int)CollectionPriority.High,
                        AvailableAt = now,
                        CreatedAt = now,
                        UpdatedAt = now,
                        DispatchGeneration = 1,
                        MetadataJson = target.AttributesJson,
                    };
                    db.Requests.Add(request);
                    db.Tasks.Add(task);
                    db.ActiveTasks.Add(new CollectionActiveTaskEntity
                    { ResourcePk = target.ResourcePk, DefinitionId = "race-detail", TaskId = task.TaskId });
                    db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
                    {
                        OutboxId = Guid.NewGuid(),
                        TaskId = task.TaskId,
                        DispatchGeneration = 1,
                        AvailableAt = now,
                        CreatedAt = now,
                    });
                    supplementCount++;
                }
            }
            db.Resources.RemoveRange(legacy);
            var oldDefinitions = await db.Definitions.Where(x => x.DefinitionId == "race-card"
                || x.DefinitionId == "race-result").ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var definition in oldDefinitions) definition.Enabled = false;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(false, legacy.Count, mapped.Select(x => (x.Source.Provider, x.Id)).Distinct().Count(),
                requestCount, taskCount, attemptCount, locationCount, stateCount, supplementCount, []);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<CollectionRequestBatchOutcome>> RequestManyAsync(string batchId,
        IReadOnlyList<CollectionRequestBatchItem> items, DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(batchId)) throw new ArgumentException("Batch id is required.", nameof(batchId));
        if (items.Count is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(items));
        if (items.Select(item => item.ItemKey).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new ArgumentException("Batch item keys must be unique.", nameof(items));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var outcomes = new List<CollectionRequestBatchOutcome>(items.Count);
            foreach (var item in items)
            {
                try
                {
                    var fingerprint = BuildRequestFingerprint(item);
                    var batchItemId = BuildBatchItemId(batchId, item.ItemKey);
                    var binding = await db.RequestBatchBindings.AsNoTracking()
                        .SingleOrDefaultAsync(x => x.BatchItemId == batchItemId, cancellationToken)
                        .ConfigureAwait(false);
                    if (binding is not null)
                    {
                        if (!string.Equals(binding.PayloadFingerprint, fingerprint, StringComparison.Ordinal))
                        {
                            outcomes.Add(new(item.ItemKey, "Rejected", ErrorCode: "IdempotencyMismatch",
                                Message: "The item key was already used with different request content."));
                            continue;
                        }
                        outcomes.Add(new(item.ItemKey, "Reused",
                            new(binding.RequestId, binding.TaskId, false)));
                        continue;
                    }
                    var receipt = await RequestCoreAsync(db, item.Resource, item.Definition, item.RequestedRevision,
                        item.Reason, requestedAt, item.Lane, item.Priority, item.ExplicitUrl,
                        batchItemId, item.EffectiveDate, item.Attributes, fingerprint,
                        cancellationToken)
                        .ConfigureAwait(false);
                    db.RequestBatchBindings.Add(new CollectionRequestBatchBindingEntity
                    {
                        BatchItemId = batchItemId,
                        PayloadFingerprint = fingerprint,
                        RequestId = receipt.RequestId,
                        TaskId = receipt.TaskId,
                    });
                    outcomes.Add(new(item.ItemKey, receipt.CreatedTask ? "Created" : "Reused", receipt));
                }
                catch (CollectionResourceSuppressedException)
                {
                    outcomes.Add(new(item.ItemKey, "Rejected", ErrorCode: "ResourceSuppressed",
                        Message: "The resource is suppressed."));
                }
                catch (CollectionRequestIdempotencyMismatchException)
                {
                    outcomes.Add(new(item.ItemKey, "Rejected", ErrorCode: "IdempotencyMismatch",
                        Message: "The item key was already used with different request content."));
                }
                catch (ArgumentException)
                {
                    outcomes.Add(new(item.ItemKey, "Rejected", ErrorCode: "InvalidRequest",
                        Message: "The request item is invalid."));
                }
                catch (InvalidOperationException)
                {
                    outcomes.Add(new(item.ItemKey, "Rejected", ErrorCode: "InvalidRequest",
                        Message: "The request item cannot be processed."));
                }
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return outcomes;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            throw new InvalidOperationException("A resource already has an active collection task.", ex);
        }
        finally { _gate.Release(); }
    }

    private static string BuildRequestFingerprint(CollectionRequestBatchItem item)
    {
        var attributes = item.Attributes?.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var payload = JsonSerializer.Serialize(new
        {
            Resource = item.Resource.Normalize(),
            item.Definition.Value,
            item.RequestedRevision,
            item.Reason,
            item.Lane,
            item.Priority,
            ExplicitUrl = item.ExplicitUrl?.AbsoluteUri,
            item.EffectiveDate,
            attributes,
        });
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(payload)));
    }

    private static string BuildBatchItemId(string batchId, string itemKey)
        => $"{batchId.Length}:{batchId}{itemKey}";

    public async Task<CollectionResourceSuppressionResult> SuppressResourceAsync(
        ResourceKey resource, string reason, string repairId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        resource = resource.Normalize();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var suppression = await db.ResourceSuppressions.SingleOrDefaultAsync(x =>
                x.Type == resource.Type && x.Provider == resource.Provider && x.ResourceId == resource.Id,
                cancellationToken).ConfigureAwait(false);
            if (suppression is null)
            {
                db.ResourceSuppressions.Add(new CollectionResourceSuppressionEntity
                {
                    Type = resource.Type,
                    Provider = resource.Provider,
                    ResourceId = resource.Id,
                    Reason = reason,
                    RepairId = repairId,
                    CreatedAt = now,
                });
            }
            else if (!string.Equals(suppression.RepairId, repairId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Resource {resource} is suppressed by another operation.");
            }

            var resourcePk = await db.Resources.Where(x => x.Type == resource.Type
                    && x.Provider == resource.Provider && x.ResourceId == resource.Id)
                .Select(x => (long?)x.ResourcePk).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            var cancelled = 0;
            var running = 0;
            if (resourcePk is not null)
            {
                var tasks = await db.Tasks.Where(x => x.ResourcePk == resourcePk.Value
                        && x.Status != CollectionTaskStatus.Succeeded
                        && x.Status != CollectionTaskStatus.Failed
                        && x.Status != CollectionTaskStatus.Cancelled
                        && x.Status != CollectionTaskStatus.DeadLetter)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var task in tasks)
                {
                    var newlyRequested = !task.CancellationRequestedAt.HasValue;
                    task.CancellationRequestedAt ??= now;
                    task.UpdatedAt = now;
                    if (newlyRequested) task.DispatchGeneration++;
                    if (task.Status == CollectionTaskStatus.Running)
                    {
                        if (newlyRequested) running++;
                        continue;
                    }
                    task.Status = CollectionTaskStatus.Cancelled;
                    task.FinishedAt = now;
                    cancelled++;
                }
                var activeTaskIds = tasks.Where(x => x.Status == CollectionTaskStatus.Cancelled)
                    .Select(x => x.TaskId).ToArray();
                var active = await db.ActiveTasks.Where(x => activeTaskIds.Contains(x.TaskId))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                db.ActiveTasks.RemoveRange(active);
                var outbox = await db.DispatchOutbox.Where(x => activeTaskIds.Contains(x.TaskId)
                        && x.DispatchedAt == null).ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var item in outbox) item.DispatchedAt = now;
                var states = await db.States.Where(x => x.ResourcePk == resourcePk.Value)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var state in states)
                {
                    state.Status = CollectionStateStatus.Unavailable;
                    state.NextCollectionAt = null;
                    state.UpdatedAt = now;
                }
                var resourceTaskIds = await db.Tasks.Where(x => x.ResourcePk == resourcePk.Value)
                    .Select(x => x.TaskId).ToArrayAsync(cancellationToken).ConfigureAwait(false);
                var failures = await db.FailureNotifications.Where(x => resourceTaskIds.Contains(x.TaskId)
                        && (x.ResolutionStatus == CollectionFailureResolutionStatus.Open
                            || x.ResolutionStatus == CollectionFailureResolutionStatus.RecoveryInProgress))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var failure in failures)
                {
                    failure.ResolutionStatus = CollectionFailureResolutionStatus.Superseded;
                    failure.ResolvedAt = now;
                }
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(cancelled, running);
        }
        finally { _gate.Release(); }
    }

    public async Task<CollectionResourceSuppressionPreview> GetResourceSuppressionPreviewAsync(
        ResourceKey resource, CancellationToken cancellationToken = default)
    {
        resource = resource.Normalize();
        await using var db = CreateDbContext();
        var resourcePk = await db.Resources.AsNoTracking().Where(x => x.Type == resource.Type
                && x.Provider == resource.Provider && x.ResourceId == resource.Id)
            .Select(x => (long?)x.ResourcePk).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (resourcePk is null) return new(0, 0);
        var statuses = await db.Tasks.AsNoTracking().Where(x => x.ResourcePk == resourcePk.Value
                && x.Status != CollectionTaskStatus.Succeeded
                && x.Status != CollectionTaskStatus.Failed
                && x.Status != CollectionTaskStatus.Cancelled
                && x.Status != CollectionTaskStatus.DeadLetter)
            .Select(x => x.Status).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new(statuses.Count(x => x != CollectionTaskStatus.Running),
            statuses.Count(x => x == CollectionTaskStatus.Running));
    }

    public async Task<IReadOnlyList<CollectionBulkTarget>> ExcludeSuppressedResourcesAsync(
        IEnumerable<CollectionBulkTarget> targets, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeBulkTargets(targets);
        if (normalized.Count == 0) return normalized;
        await using var db = CreateDbContext();
        var suppressions = await db.ResourceSuppressions.AsNoTracking().ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return normalized.Where(target => !suppressions.Any(x => x.Type == target.Resource.Type
                && x.Provider == target.Resource.Provider && x.ResourceId == target.Resource.Id))
            .ToList();
    }

    public async Task<bool> HeartbeatAsync(Guid taskId, string leaseToken, DateTimeOffset now,
        TimeSpan extension, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var task = await db.Tasks.SingleOrDefaultAsync(x => x.TaskId == taskId, cancellationToken);
            if (task is null || task.Status != CollectionTaskStatus.Running || task.CancellationRequestedAt.HasValue
                || !string.Equals(task.LeaseToken, leaseToken, StringComparison.Ordinal)) return false;
            task.LeaseExpiresAt = now.Add(extension);
            task.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> ReconcileDeadLetterAsync(Guid taskId, long dispatchGeneration, DateTimeOffset now,
        string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var task = await db.Tasks.SingleOrDefaultAsync(x => x.TaskId == taskId, cancellationToken);
            if (task is null || task.DispatchGeneration != dispatchGeneration
                || task.Status is CollectionTaskStatus.Succeeded or CollectionTaskStatus.Failed
                    or CollectionTaskStatus.Cancelled or CollectionTaskStatus.DeadLetter) return false;
            if (task.Status == CollectionTaskStatus.Running)
            {
                var attempt = await db.Attempts.SingleAsync(x => x.TaskId == taskId
                    && x.AttemptNumber == task.AttemptCount, cancellationToken);
                attempt.Result = CollectionAttemptResult.PermanentFailure;
                attempt.ErrorCode = "DeadLetterQueue";
                attempt.ErrorMessage = errorMessage;
                attempt.FinishedAt = now;
            }
            task.Status = CollectionTaskStatus.DeadLetter;
            task.FinishedAt = now;
            task.LeaseToken = null;
            task.LeaseExpiresAt = null;
            task.UpdatedAt = now;
            var state = await db.States.SingleAsync(x => x.ResourcePk == task.ResourcePk
                && x.DefinitionId == task.DefinitionId, cancellationToken);
            state.Status = CollectionStateStatus.Failed;
            state.UpdatedAt = now;
            var active = await db.ActiveTasks.SingleOrDefaultAsync(x => x.TaskId == taskId, cancellationToken);
            if (active is not null) db.ActiveTasks.Remove(active);
            await QueueFailureNotificationAsync(db, task, "DeadLetterQueue", errorMessage, now, true, cancellationToken)
                .ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<CollectionWatchdogResult> RunWatchdogAsync(DateTimeOffset now, int maxDispatchAttempts,
        TimeSpan dispatchGrace, CancellationToken cancellationToken = default)
    {
        // A successful queue send is durable evidence that a Ready task is waiting in the transport.
        // Queue backlog duration is unbounded, so elapsed time cannot distinguish a lost message from
        // a healthy message that has not reached a worker yet. Redelivering here creates duplicates and
        // can exhaust DispatchGeneration before the original message is ever received. Transport delivery
        // failures are reconciled through the DLQ; only expired acquired leases are recovered here.
        _ = maxDispatchAttempts;
        _ = dispatchGrace;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var runningBefore = await db.Tasks.CountAsync(x => x.Status == CollectionTaskStatus.Running, cancellationToken);
            await ReclaimExpiredAsync(db, now, cancellationToken).ConfigureAwait(false);
            var reclaimed = runningBefore - await db.Tasks.CountAsync(x => x.Status == CollectionTaskStatus.Running, cancellationToken);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(reclaimed, 0, 0);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<PendingCollectionFailureNotification>> GetPendingFailureNotificationsAsync(
        DateTimeOffset now, int maxCount, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var rows = await (from notification in db.FailureNotifications.AsNoTracking()
                          join task in db.Tasks.AsNoTracking() on notification.TaskId equals task.TaskId
                          join resource in db.Resources.AsNoTracking() on task.ResourcePk equals resource.ResourcePk
                          where notification.PublishedAt == null
                          select new { notification, task, resource }).ToListAsync(cancellationToken);
        return rows.Where(x => x.notification.AvailableAt <= now).OrderBy(x => x.notification.AvailableAt)
            .Take(Math.Max(1, maxCount)).Select(x => new PendingCollectionFailureNotification(
                x.notification.NotificationId, x.task.TaskId,
                new(x.resource.Type, x.resource.Provider, x.resource.ResourceId), new(x.task.DefinitionId),
                Enum.Parse<CollectionTaskStatus>(x.notification.Status), x.notification.ErrorCode,
                x.notification.ErrorMessage, x.notification.AttemptCount, x.notification.FailedAt)).ToList();
    }

    public Task<IReadOnlyList<PendingCollectionFailureNotification>> GetUnpublishedFailureNotificationsAsync(
        DateTimeOffset now, int maxCount, CancellationToken cancellationToken = default)
        => GetPendingFailureNotificationsAsync(now, maxCount, cancellationToken);

    public async Task<PendingCollectionFailureNotification?> GetUnpublishedFailureNotificationAsync(
        Guid notificationId, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var row = await (from notification in db.FailureNotifications.AsNoTracking()
                         join task in db.Tasks.AsNoTracking() on notification.TaskId equals task.TaskId
                         join resource in db.Resources.AsNoTracking() on task.ResourcePk equals resource.ResourcePk
                         where notification.NotificationId == notificationId && notification.PublishedAt == null
                         select new { notification, task, resource }).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new PendingCollectionFailureNotification(
            row.notification.NotificationId, row.task.TaskId,
            new(row.resource.Type, row.resource.Provider, row.resource.ResourceId), new(row.task.DefinitionId),
            Enum.Parse<CollectionTaskStatus>(row.notification.Status), row.notification.ErrorCode,
            row.notification.ErrorMessage, row.notification.AttemptCount, row.notification.FailedAt);
    }

    public async Task<IReadOnlyList<PendingCollectionFailureNotification>> GetActionableFailureNotificationsAsync(
        DateTimeOffset now, int maxCount, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var rows = await FailureQuery(db).Where(x => x.Notification.ResolutionStatus
                == CollectionFailureResolutionStatus.Open).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Where(x => x.Notification.AvailableAt <= now).OrderBy(x => x.Notification.AvailableAt)
            .Take(Math.Max(1, maxCount)).Select(ToFailure).ToList();
    }

    public async Task<IReadOnlyList<PendingCollectionFailureNotification>> GetFailureNotificationsAsync(
        IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken = default)
    {
        var ids = notificationIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        await using var db = CreateDbContext();
        var rows = await FailureQuery(db).Where(x => ids.Contains(x.Notification.NotificationId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToFailure).ToList();
    }

    public async Task<CollectionFailureDismissalResult> DismissFailureNotificationsAsync(
        IReadOnlyCollection<Guid> notificationIds, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var ids = notificationIds.Distinct().ToArray();
        if (ids.Length == 0) throw new ArgumentException("At least one notification is required.", nameof(notificationIds));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var rows = await db.FailureNotifications.Where(x => ids.Contains(x.NotificationId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (rows.Count != ids.Length)
                throw new KeyNotFoundException("One or more failure notifications were not found.");
            if (rows.Any(x => x.ResolutionStatus == CollectionFailureResolutionStatus.RecoveryInProgress))
                return new(ids.Length, 0, rows.Count(x => x.ResolutionStatus != CollectionFailureResolutionStatus.Open), true);

            var open = rows.Where(x => x.ResolutionStatus == CollectionFailureResolutionStatus.Open).ToArray();
            foreach (var row in open)
            {
                row.ResolutionStatus = CollectionFailureResolutionStatus.Superseded;
                row.ResolvedAt = now;
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(ids.Length, open.Length, ids.Length - open.Length);
        }
        finally { _gate.Release(); }
    }

    public async Task<CollectionFailureGroupMatch> GetActionableFailureGroupAsync(string groupKey,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupKey))
            return new(0, []);
        var notifications = await GetActionableFailureNotificationsAsync(now, int.MaxValue, cancellationToken)
            .ConfigureAwait(false);
        var matchingGroups = notifications.GroupBy(x => new
        {
            Definition = x.Definition.Value,
            x.Status,
            ErrorCode = x.ErrorCode ?? string.Empty,
        })
            .Where(x => string.Equals(CollectionFailureGrouping.CreateKey(
                x.Key.Definition, x.Key.Status, x.Key.ErrorCode), groupKey, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.ToList())
            .ToList();
        return new(matchingGroups.Count, matchingGroups.Count == 1 ? matchingGroups[0] : []);
    }

    public async Task<CollectionFailureGroupPage?> GetActionableFailureGroupPageAsync(string groupKey,
        DateTimeOffset now, string? search = null, int page = 1, int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var match = await GetActionableFailureGroupAsync(groupKey, now, cancellationToken).ConfigureAwait(false);
        if (match.MatchingGroupCount == 0) return null;
        if (match.MatchingGroupCount > 1)
            throw new InvalidOperationException("The failure group key matches multiple groups.");

        await using var db = CreateDbContext();
        var taskIds = match.Notifications.Select(x => x.TaskId).ToArray();
        var attemptRows = await db.Attempts.AsNoTracking().Where(x => taskIds.Contains(x.TaskId))
            .OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.AttemptId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var latestAttempts = attemptRows.GroupBy(x => x.TaskId).ToDictionary(x => x.Key, x => x.First());
        var requestIds = await db.Tasks.AsNoTracking().Where(x => taskIds.Contains(x.TaskId))
            .Select(x => new { x.TaskId, x.RequestId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var requestIdByTask = requestIds.ToDictionary(x => x.TaskId, x => x.RequestId);
        var ids = requestIds.Select(x => x.RequestId).Distinct().ToArray();
        var explicitUrls = await db.Requests.AsNoTracking().Where(x => ids.Contains(x.RequestId))
            .Select(x => new { x.RequestId, x.ExplicitUrl }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var explicitUrlByRequest = explicitUrls.ToDictionary(x => x.RequestId, x => x.ExplicitUrl);

        var targets = match.Notifications.Select(notification =>
        {
            latestAttempts.TryGetValue(notification.TaskId, out var attempt);
            string? explicitUrl = null;
            if (requestIdByTask.TryGetValue(notification.TaskId, out var requestId))
                explicitUrlByRequest.TryGetValue(requestId, out explicitUrl);
            return new CollectionFailureTarget(notification.NotificationId, notification.TaskId,
                notification.Resource, notification.Definition, notification.Status, notification.ErrorCode,
                notification.ErrorMessage, notification.AttemptCount, notification.FailedAt,
                attempt?.RequestedUrl ?? explicitUrl, attempt?.FinalUrl, attempt?.HttpStatusCode,
                attempt?.PageIdentification, attempt?.ExecutionBatchId, attempt?.LambdaRequestId);
        }).ToList();

        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (search is not null)
            targets = targets.Where(x => Contains(x.Resource.Type.ToString(), search)
                || Contains(x.Resource.Provider, search) || Contains(x.Resource.Id, search)
                || Contains(x.ErrorCode, search) || Contains(x.ErrorMessage, search)
                || Contains(x.RequestedUrl, search) || Contains(x.FinalUrl, search)).ToList();
        targets = targets.OrderByDescending(x => x.FailedAt).ThenBy(x => x.Resource.Type)
            .ThenBy(x => x.Resource.Provider).ThenBy(x => x.Resource.Id).ToList();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var group = CollectionFailureGrouping.Build(match.Notifications).Single() with
        {
            NotificationIds = [],
        };
        return new(group, targets.Count, page, pageSize, search,
            targets.Skip((int)offset).Take(pageSize).ToList());

        static bool Contains(string? value, string query) =>
            value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    }

    public async Task MarkFailureNotificationPublishedAsync(Guid notificationId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var item = await db.FailureNotifications.SingleAsync(x => x.NotificationId == notificationId, cancellationToken);
            item.PublishedAt = now;
            item.PublishAttemptCount++;
            item.LastPublishError = null;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task MarkFailureNotificationPublishFailedAsync(Guid notificationId, DateTimeOffset now,
        string error, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var item = await db.FailureNotifications.SingleAsync(x => x.NotificationId == notificationId,
                cancellationToken);
            item.AvailableAt = now.AddSeconds(5);
            item.PublishAttemptCount++;
            item.LastPublishError = error;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<BackfillBatchSnapshot> CreateOrResumeBackfillBatchAsync(string batchId, string provider,
        DateOnly from, DateOnly to, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(batchId)) throw new ArgumentException("Batch id is required.", nameof(batchId));
        if (from > to) throw new ArgumentException("Backfill start date must be on or before end date.", nameof(from));
        provider = provider.Trim().ToUpperInvariant();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var batch = await db.BackfillBatches.SingleOrDefaultAsync(x => x.BatchId == batchId, cancellationToken);
            if (batch is null)
            {
                db.BackfillBatches.Add(new BackfillBatchEntity
                {
                    BatchId = batchId,
                    Provider = provider,
                    From = from,
                    To = to,
                    CreatedAt = now
                });
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (batch.Provider != provider || batch.From != from || batch.To != to)
                throw new InvalidOperationException($"Backfill batch '{batchId}' already exists with another range.");
        }
        finally { _gate.Release(); }

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resource = new ResourceKey(ResourceType.Race, provider, $"backfill:{date:yyyyMMdd}");
            if (await GetStateAsync(resource, new("race-discovery"), cancellationToken).ConfigureAwait(false) is not null)
                continue;
            await RequestAsync(resource, new("race-discovery"), 1, CollectionReason.Backfill, now,
                CollectionLane.Background, (int)CollectionPriority.Background, batchId: batchId,
                effectiveDate: date, attributes: new Dictionary<string, string>
                {
                    ["batchId"] = batchId,
                    ["backfillDate"] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var batch = await db.BackfillBatches.SingleAsync(x => x.BatchId == batchId, cancellationToken);
            batch.ExpansionCompletedAt ??= now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        return (await GetBackfillBatchAsync(batchId, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<RacePeriodRecollectionReceipt> CreateOrResumeRacePeriodRecollectionAsync(
        string batchId, string provider, DateOnly from, DateOnly to, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(batchId)) throw new ArgumentException("Batch id is required.", nameof(batchId));
        if (from > to) throw new ArgumentException("Recollection start date must be on or before end date.", nameof(from));
        provider = provider.Trim().ToUpperInvariant();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var batch = await db.BackfillBatches.SingleOrDefaultAsync(x => x.BatchId == batchId, cancellationToken);
            if (batch is null)
            {
                db.BackfillBatches.Add(new BackfillBatchEntity
                {
                    BatchId = batchId,
                    Provider = provider,
                    From = from,
                    To = to,
                    CreatedAt = now
                });
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (batch.Provider != provider || batch.From != from || batch.To != to)
                throw new InvalidOperationException($"Recollection batch '{batchId}' already exists with another range.");
        }
        finally { _gate.Release(); }

        var created = 0;
        var reused = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resource = new ResourceKey(ResourceType.Race, provider, $"recollection:{date:yyyyMMdd}");
            var receipt = await RequestAsync(resource, new("race-discovery"), 1,
                CollectionReason.PeriodRecollection, now, CollectionLane.Background,
                (int)CollectionPriority.Background, batchId: batchId, effectiveDate: date,
                attributes: new Dictionary<string, string>
                {
                    ["batchId"] = batchId,
                    ["backfillDate"] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (receipt.CreatedTask) created++; else reused++;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var batch = await db.BackfillBatches.SingleAsync(x => x.BatchId == batchId, cancellationToken);
            batch.ExpansionCompletedAt ??= now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }

        var snapshot = (await GetBackfillBatchAsync(batchId, cancellationToken).ConfigureAwait(false))!;
        return new(snapshot, created, reused);
    }

    public async Task<BackfillBatchSnapshot?> GetBackfillBatchAsync(string batchId,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var batch = await db.BackfillBatches.AsNoTracking().SingleOrDefaultAsync(x => x.BatchId == batchId,
            cancellationToken).ConfigureAwait(false);
        if (batch is null) return null;
        var rows = await (from request in db.Requests.AsNoTracking()
                          join task in db.Tasks.AsNoTracking() on request.RequestId equals task.RequestId
                          join resource in db.Resources.AsNoTracking() on task.ResourcePk equals resource.ResourcePk
                          where request.BatchId == batchId
                          select new { task, resource }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var taskIds = rows.Select(x => x.task.TaskId).ToArray();
        var attempts = await db.Attempts.AsNoTracking().Where(x => taskIds.Contains(x.TaskId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var failedRows = rows.Where(x => x.task.Status is CollectionTaskStatus.Failed or CollectionTaskStatus.DeadLetter)
            .ToList();
        var failedResourcePks = failedRows.Select(x => x.task.ResourcePk).Distinct().ToArray();
        var earliestFailure = failedRows.Count == 0 ? DateTimeOffset.MaxValue
            : failedRows.Min(x => x.task.CreatedAt);
        var laterSuccesses = await (from task in db.Tasks.AsNoTracking()
                                    join request in db.Requests.AsNoTracking() on task.RequestId equals request.RequestId
                                    where task.Status == CollectionTaskStatus.Succeeded && failedResourcePks.Contains(task.ResourcePk)
                                        && task.CreatedAt >= earliestFailure
                                    select new { task.ResourcePk, task.DefinitionId, task.CreatedAt, request.Reason })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var holes = failedRows
            .Where(x => !laterSuccesses.Any(success => success.ResourcePk == x.task.ResourcePk
                && success.DefinitionId == x.task.DefinitionId
                && (success.CreatedAt > x.task.CreatedAt
                    || success.CreatedAt == x.task.CreatedAt && success.Reason == CollectionReason.Recovery)))
            .Select(x =>
            {
                var attempt = attempts.Where(a => a.TaskId == x.task.TaskId)
                    .OrderByDescending(a => a.AttemptNumber).FirstOrDefault();
                return new BackfillHole(new(x.resource.Type, x.resource.Provider, x.resource.ResourceId),
                    new(x.task.DefinitionId), x.task.Status, attempt?.ErrorCode, attempt?.ErrorMessage);
            }).ToList();
        var expected = batch.To.DayNumber - batch.From.DayNumber + 1;
        var discoveryDays = rows.Count(x => x.task.DefinitionId == "race-discovery");
        return new(batch.BatchId, batch.From, batch.To, expected, discoveryDays,
            rows.Count(x => x.task.Status is CollectionTaskStatus.Pending or CollectionTaskStatus.Ready
                or CollectionTaskStatus.RetryWaiting or CollectionTaskStatus.WaitingDiscovery),
            rows.Count(x => x.task.Status == CollectionTaskStatus.Running),
            rows.Count(x => x.task.Status == CollectionTaskStatus.Succeeded),
            holes.Count, holes, batch.CreatedAt, batch.ExpansionCompletedAt);
    }

    public async Task<IReadOnlyList<BackfillBatchSnapshot>> GetBackfillBatchesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var ids = (await db.BackfillBatches.AsNoTracking()
                .Select(x => new { x.BatchId, x.CreatedAt }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .OrderByDescending(x => x.CreatedAt).Select(x => x.BatchId).ToList();
        var results = new List<BackfillBatchSnapshot>(ids.Count);
        foreach (var id in ids)
            if (await GetBackfillBatchAsync(id, cancellationToken).ConfigureAwait(false) is { } item) results.Add(item);
        return results;
    }

    public async Task<int> ResumeIncompleteBackfillBatchesAsync(DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var incomplete = await db.BackfillBatches.AsNoTracking().Where(x => x.ExpansionCompletedAt == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var batch in incomplete)
            await CreateOrResumeBackfillBatchAsync(batch.BatchId, batch.Provider, batch.From, batch.To, now,
                cancellationToken).ConfigureAwait(false);
        return incomplete.Count;
    }

    public async Task<CollectionInitializationReport> InitializeFromDomainDataAsync(
        IReadOnlyCollection<CollectionInitializationSeed> seeds, bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var normalized = seeds.Select(x => x with { Resource = x.Resource.Normalize() })
            .GroupBy(x => new { x.Resource, x.Definition })
            .Select(x => x.OrderByDescending(y => y.CollectedAt).First()).ToList();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var resourcesAdded = 0;
            var statesAdded = 0;
            var locationsAdded = 0;
            foreach (var seed in normalized)
            {
                var definition = await db.Definitions.AsNoTracking().SingleOrDefaultAsync(
                    x => x.DefinitionId == seed.Definition.Value, cancellationToken)
                    ?? throw new InvalidOperationException($"Collection definition {seed.Definition} is not registered.");
                if (definition.ResourceType != seed.Resource.Type || seed.AppliedRevision > definition.CurrentRevision)
                    throw new InvalidOperationException($"Initialization seed is incompatible with {seed.Definition}.");
                var resource = await db.Resources.SingleOrDefaultAsync(x => x.Type == seed.Resource.Type
                    && x.Provider == seed.Resource.Provider && x.ResourceId == seed.Resource.Id, cancellationToken);
                if (resource is null)
                {
                    resourcesAdded++;
                    if (dryRun)
                    {
                        statesAdded++;
                        if (seed.SourceUrl is not null) locationsAdded++;
                        continue;
                    }
                    resource = new CollectionResourceEntity
                    {
                        Type = seed.Resource.Type,
                        Provider = seed.Resource.Provider,
                        ResourceId = seed.Resource.Id,
                        EffectiveDate = seed.EffectiveDate,
                        AttributesJson = JsonSerializer.Serialize(seed.Attributes),
                        CreatedAt = seed.CollectedAt
                    };
                    db.Resources.Add(resource);
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                if (!await db.States.AnyAsync(x => x.ResourcePk == resource.ResourcePk
                    && x.DefinitionId == seed.Definition.Value, cancellationToken))
                {
                    statesAdded++;
                    if (!dryRun) db.States.Add(new CollectionStateEntity
                    {
                        ResourcePk = resource.ResourcePk,
                        DefinitionId = seed.Definition.Value,
                        AppliedRevision = seed.IsComplete ? seed.AppliedRevision : 0,
                        RequiredRevision = seed.AppliedRevision,
                        LastCollectedAt = seed.CollectedAt,
                        NextCollectionAt = seed.IsComplete ? null : seed.CollectedAt,
                        Status = seed.IsComplete ? CollectionStateStatus.Current : CollectionStateStatus.RefreshDue,
                        UpdatedAt = seed.CollectedAt
                    });
                }
                if (seed.SourceUrl is not null && !await db.Locations.AnyAsync(x => x.ResourcePk == resource.ResourcePk
                    && x.DefinitionId == seed.Definition.Value && x.Url == seed.SourceUrl.AbsoluteUri, cancellationToken))
                {
                    locationsAdded++;
                    if (!dryRun) db.Locations.Add(new ResourceLocationEntity
                    {
                        ResourcePk = resource.ResourcePk,
                        DefinitionId = seed.Definition.Value,
                        Url = seed.SourceUrl.AbsoluteUri,
                        Source = ResourceLocationSource.Discovered,
                        Status = ResourceLocationStatus.Active,
                        DiscoveredAt = seed.CollectedAt,
                        LastVerifiedAt = seed.CollectedAt
                    });
                }
            }
            if (!dryRun) await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            var months = normalized.Where(x => x.EffectiveDate.HasValue)
                .Select(x => $"{x.EffectiveDate!.Value:yyyy-MM}").Distinct().Order().ToList();
            return new(dryRun, normalized.Count, resourcesAdded, statesAdded, locationsAdded, months);
        }
        finally { _gate.Release(); }
    }

    private static bool IsRetryable(CollectionAttemptResult result) => result is
        CollectionAttemptResult.TransientFailure or CollectionAttemptResult.ResourceNotYetAvailable
        or CollectionAttemptResult.AccessLimited;

    private static TimeSpan DefaultRetryDelay(CollectionAttemptResult result, int attemptCount)
    {
        var seconds = Math.Min(900, 15 * Math.Pow(2, Math.Clamp(attemptCount - 1, 0, 6)));
        return result == CollectionAttemptResult.AccessLimited
            ? TimeSpan.FromSeconds(Math.Max(60, seconds)) : TimeSpan.FromSeconds(seconds);
    }

    private static async Task QueueFailureNotificationAsync(CollectionPlatformDbContext db,
        CollectionTaskEntity task, string? errorCode, string? errorMessage, DateTimeOffset now,
        bool pausePipeline, CancellationToken cancellationToken)
    {
        var previous = await db.FailureNotifications.Where(x => x.ResolutionStatus
            == CollectionFailureResolutionStatus.Open || x.ResolutionStatus
            == CollectionFailureResolutionStatus.RecoveryInProgress)
            .Join(db.Tasks.Where(x => x.ResourcePk == task.ResourcePk && x.DefinitionId == task.DefinitionId),
                x => x.TaskId, x => x.TaskId, (failure, _) => failure).ToListAsync(cancellationToken);
        foreach (var item in previous)
        {
            item.ResolutionStatus = CollectionFailureResolutionStatus.Superseded;
            item.ResolvedAt = now;
        }
        var notificationId = Guid.NewGuid();
        db.FailureNotifications.Add(new CollectionFailureNotificationEntity
        {
            NotificationId = notificationId,
            TaskId = task.TaskId,
            Status = task.Status.ToString(),
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            AttemptCount = task.AttemptCount,
            FailedAt = now,
            AvailableAt = now,
            ResolutionStatus = CollectionFailureResolutionStatus.Open,
        });
        if (pausePipeline)
        {
            var control = await db.Controls.SingleOrDefaultAsync(x => x.ControlId == "pipeline", cancellationToken);
            if (control is null)
            {
                control = new CollectionPlatformControlEntity { ControlId = "pipeline" };
                db.Controls.Add(control);
            }
            if (!control.IsPaused)
            {
                control.IsPaused = true;
                control.Reason = $"Unexpected collection failure notification {notificationId:D}: "
                    + $"Task={task.TaskId:D}; Error={errorCode ?? "Unknown"}; {errorMessage}";
                control.UpdatedAt = now;
            }
        }
    }

    private static IQueryable<FailureRow> FailureQuery(CollectionPlatformDbContext db, long? resourcePk = null,
        string? definitionId = null) =>
        from notification in db.FailureNotifications.AsNoTracking()
        join task in db.Tasks.AsNoTracking() on notification.TaskId equals task.TaskId
        join resource in db.Resources.AsNoTracking() on task.ResourcePk equals resource.ResourcePk
        where (!resourcePk.HasValue || task.ResourcePk == resourcePk.Value)
            && (definitionId == null || task.DefinitionId == definitionId)
        select new FailureRow { Notification = notification, Task = task, Resource = resource };

    private static PendingCollectionFailureNotification ToFailure(FailureRow row) => new(
        row.Notification.NotificationId, row.Task.TaskId,
        new(row.Resource.Type, row.Resource.Provider, row.Resource.ResourceId), new(row.Task.DefinitionId),
        Enum.Parse<CollectionTaskStatus>(row.Notification.Status), row.Notification.ErrorCode,
        row.Notification.ErrorMessage, row.Notification.AttemptCount, row.Notification.FailedAt,
        row.Notification.ResolutionStatus, row.Notification.RecoveryTaskId,
        row.Notification.RecoveryStartedAt, row.Notification.ResolvedAt);

    private sealed class FailureRow
    {
        public required CollectionFailureNotificationEntity Notification { get; init; }
        public required CollectionTaskEntity Task { get; init; }
        public required CollectionResourceEntity Resource { get; init; }
    }

    private static async Task StartFailureRecoveryAsync(CollectionPlatformDbContext db, long resourcePk,
        string definitionId, Guid recoveryTaskId, DateTimeOffset now, CancellationToken token)
    {
        var failures = await db.FailureNotifications.Where(x => x.ResolutionStatus == CollectionFailureResolutionStatus.Open
                || x.ResolutionStatus == CollectionFailureResolutionStatus.RecoveryInProgress)
            .Join(db.Tasks.Where(x => x.ResourcePk == resourcePk && x.DefinitionId == definitionId),
                x => x.TaskId, x => x.TaskId, (failure, _) => failure).ToListAsync(token);
        foreach (var failure in failures)
        {
            failure.ResolutionStatus = CollectionFailureResolutionStatus.RecoveryInProgress;
            failure.RecoveryTaskId = recoveryTaskId;
            failure.RecoveryStartedAt = now;
        }
    }

    private static async Task ResolveFailuresAsync(CollectionPlatformDbContext db, long resourcePk,
        string definitionId, DateTimeOffset now, CancellationToken token)
    {
        var failures = await db.FailureNotifications.Where(x => x.ResolutionStatus
                == CollectionFailureResolutionStatus.Open || x.ResolutionStatus
                == CollectionFailureResolutionStatus.RecoveryInProgress)
            .Join(db.Tasks.Where(x => x.ResourcePk == resourcePk && x.DefinitionId == definitionId),
                x => x.TaskId, x => x.TaskId, (failure, _) => failure).ToListAsync(token);
        foreach (var failure in failures)
        {
            failure.ResolutionStatus = CollectionFailureResolutionStatus.Resolved;
            failure.ResolvedAt = now;
        }
    }

    private static async Task<int> ReopenFailuresAsync(CollectionPlatformDbContext db, Guid recoveryTaskId,
        CancellationToken token)
    {
        var failures = await db.FailureNotifications.Where(x => x.RecoveryTaskId == recoveryTaskId
            && x.ResolutionStatus == CollectionFailureResolutionStatus.RecoveryInProgress).ToListAsync(token);
        foreach (var failure in failures)
        {
            failure.ResolutionStatus = CollectionFailureResolutionStatus.Open;
            failure.RecoveryTaskId = null;
            failure.RecoveryStartedAt = null;
        }
        return failures.Count;
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
            RevisionImpactScopeType.SpecificResources => MatchesSpecificResource(candidate.Resource, impact.ScopePayload),
            RevisionImpactScopeType.DateRange => MatchesDateRange(candidate.EffectiveDate, impact.ScopePayload),
            RevisionImpactScopeType.NamedCondition => conditions[impact.ScopePayload].Matches(candidate),
            _ => false,
        };

    private static bool MatchesSpecificResource(ResourceKey resource, string payload)
    {
        try
        {
            var keys = JsonSerializer.Deserialize<ResourceKey[]>(payload);
            if (keys is not null)
                return keys.Select(x => x.Normalize()).Contains(resource.Normalize());
        }
        catch (JsonException)
        {
            // Older revision impacts stored resource ids only. Keep them readable during migration.
        }
        return (JsonSerializer.Deserialize<string[]>(payload) ?? []).Contains(resource.Id, StringComparer.Ordinal);
    }

    private static RevisionResourceCandidate ToRevisionCandidate(CollectionResourceEntity resource)
        => new(new(resource.Type, resource.Provider, resource.ResourceId), resource.EffectiveDate,
            JsonSerializer.Deserialize<Dictionary<string, string>>(resource.AttributesJson) ?? []);

    private static async Task<List<RevisionResourceCandidate>> LoadRevisionCandidatesAsync(
        CollectionPlatformDbContext db, string definitionId, CancellationToken cancellationToken)
    {
        var resources = await (from state in db.States.AsNoTracking()
                               join resource in db.Resources.AsNoTracking() on state.ResourcePk equals resource.ResourcePk
                               where state.DefinitionId == definitionId
                               select resource).ToListAsync(cancellationToken).ConfigureAwait(false);
        return resources.Select(ToRevisionCandidate).ToList();
    }

    private static List<CollectionBulkTarget> NormalizeBulkTargets(IEnumerable<CollectionBulkTarget> targets)
    {
        var normalized = targets.Select(x => x with { Resource = x.Resource.Normalize() })
            .GroupBy(x => x.Resource).Select(x => x.First()).ToList();
        if (normalized.Count is < 1 or > 10_000)
            throw new ArgumentException("Bulk request must contain between 1 and 10000 distinct resources.", nameof(targets));
        if (normalized.Any(x => string.IsNullOrWhiteSpace(x.Resource.Provider)
                                || string.IsNullOrWhiteSpace(x.Resource.Id)))
            throw new ArgumentException("Every bulk resource requires provider and id.", nameof(targets));
        return normalized;
    }

    private static async Task ValidateBulkRequestAsync(CollectionPlatformDbContext db,
        CollectionDefinitionId definition, int requestedRevision, IReadOnlyCollection<CollectionBulkTarget> targets,
        CancellationToken cancellationToken)
    {
        var definitionEntity = await db.Definitions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DefinitionId == definition.Value, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Collection definition {definition} is not registered.");
        if (!definitionEntity.Enabled || targets.Any(x => x.Resource.Type != definitionEntity.ResourceType))
            throw new InvalidOperationException($"Every resource must match definition {definitionEntity.ResourceType}.");
        if (requestedRevision < 1 || requestedRevision > definitionEntity.CurrentRevision
            || !await db.Revisions.AsNoTracking().AnyAsync(x => x.DefinitionId == definition.Value
                && x.Revision == requestedRevision, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"Revision {requestedRevision} is not registered for {definition}.");
    }

    private static bool MatchesDateRange(DateOnly? date, string payload)
    {
        if (date is null) return false;
        var range = JsonSerializer.Deserialize<DateRangeImpact>(payload)
            ?? throw new InvalidOperationException("Date range impact is invalid.");
        return date >= range.From && date <= range.To;
    }

    private sealed record DateRangeImpact(DateOnly From, DateOnly To);

    private static async Task ReclaimExpiredExecutionLeasesAsync(CollectionPlatformDbContext db, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expired = await db.ExecutionLeases.Where(x => (x.Status == "StartPending" || x.Status == "Running")
                && x.LeaseExpiresAt <= now).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var lease in expired)
        {
            await ReleaseUnstartedDispatchesAsync(db, lease.DispatchEnvelopeId, cancellationToken).ConfigureAwait(false);
            lease.Status = "Expired";
            lease.FinishedAt = now;
        }
        if (expired.Count > 0) await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReleaseUnstartedDispatchesAsync(CollectionPlatformDbContext db, Guid envelopeId,
        CancellationToken cancellationToken)
    {
        var rows = await (from outbox in db.DispatchOutbox
                          join task in db.Tasks on outbox.TaskId equals task.TaskId
                          where outbox.EnvelopeId == envelopeId && outbox.DispatchGeneration == task.DispatchGeneration
                                && task.Status == CollectionTaskStatus.Ready
                          select outbox).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            row.DispatchedAt = null;
            row.ReservationToken = null;
            row.ReservedUntilUnixMilliseconds = null;
            row.EnvelopeId = null;
            row.QueueMessageId = null;
        }
    }

    private static async Task<CollectionDispatchEnvelope?> BuildExecutionEnvelopeAsync(CollectionPlatformDbContext db,
        Guid envelopeId, CancellationToken cancellationToken)
    {
        var rows = await (from outbox in db.DispatchOutbox.AsNoTracking()
                          join task in db.Tasks.AsNoTracking() on outbox.TaskId equals task.TaskId
                          join resource in db.Resources.AsNoTracking() on task.ResourcePk equals resource.ResourcePk
                          where outbox.EnvelopeId == envelopeId && outbox.DispatchGeneration == task.DispatchGeneration
                          orderby task.Priority descending, task.CreatedAt, task.TaskId
                          select new { outbox, task, resource }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0) return null;
        var first = rows[0];
        var attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(first.resource.AttributesJson) ?? [];
        CollectionDispatchCompatibilityKey compatibility;
        if (first.resource.EffectiveDate.HasValue
            && first.resource.Type is ResourceType.RaceCard or ResourceType.RaceResult or ResourceType.Race)
            compatibility = new(first.resource.Provider, new(first.task.DefinitionId), first.resource.EffectiveDate,
                first.task.Lane, CollectionDispatchGroupKind.RaceDay, first.resource.EffectiveDate.Value.ToString("yyyy-MM-dd"));
        else if (first.resource.Type == ResourceType.Horse
                 && attributes.GetValueOrDefault("weekendPriorityUntil") is { Length: > 0 } weekend)
            compatibility = new(first.resource.Provider, new(first.task.DefinitionId), first.resource.EffectiveDate,
                first.task.Lane, CollectionDispatchGroupKind.WeekendSubjects, weekend);
        else
            compatibility = new(first.resource.Provider, new(first.task.DefinitionId), first.resource.EffectiveDate,
                first.task.Lane, CollectionDispatchGroupKind.Definition, first.task.DefinitionId);
        return new(envelopeId, compatibility, rows.Select(x => new CollectionDispatchTaskReference(
            x.task.TaskId, x.outbox.DispatchGeneration)).ToArray());
    }

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
            if (task.CancellationRequestedAt.HasValue)
            {
                attempt.Result = CollectionAttemptResult.Cancelled;
                attempt.ErrorCode = "CancellationRequested";
                task.Status = CollectionTaskStatus.Cancelled;
                task.FinishedAt = now;
                task.LeaseToken = null;
                task.LeaseExpiresAt = null;
                task.UpdatedAt = now;
                var active = await db.ActiveTasks.SingleOrDefaultAsync(x => x.TaskId == task.TaskId, cancellationToken);
                if (active is not null) db.ActiveTasks.Remove(active);
                var state = await db.States.SingleAsync(x => x.ResourcePk == task.ResourcePk
                    && x.DefinitionId == task.DefinitionId, cancellationToken);
                var suppressed = await IsResourceSuppressedAsync(db, task.ResourcePk, cancellationToken)
                    .ConfigureAwait(false);
                state.Status = suppressed ? CollectionStateStatus.Unavailable : CollectionStateStatus.Unknown;
                if (suppressed) state.NextCollectionAt = null;
                state.UpdatedAt = now;
                if (!suppressed && await ReopenFailuresAsync(db, task.TaskId, cancellationToken).ConfigureAwait(false) > 0)
                    state.Status = CollectionStateStatus.Failed;
                continue;
            }
            task.Status = CollectionTaskStatus.Ready;
            task.AvailableAt = now;
            task.LeaseToken = null;
            task.LeaseExpiresAt = null;
            task.DispatchGeneration++;
            task.UpdatedAt = now;
            db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
            {
                OutboxId = Guid.NewGuid(),
                TaskId = task.TaskId,
                DispatchGeneration = task.DispatchGeneration,
                AvailableAt = now,
                CreatedAt = now,
            });
        }
        if (expired.Count > 0) await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool TryCanonicalCourse(string value, out string canonical)
    {
        canonical = value.Trim() switch
        {
            "札幌" or "Sapporo" => "Sapporo",
            "函館" or "Hakodate" => "Hakodate",
            "福島" or "Fukushima" => "Fukushima",
            "新潟" or "Niigata" => "Niigata",
            "東京" or "Tokyo" => "Tokyo",
            "中山" or "Nakayama" => "Nakayama",
            "中京" or "Chukyo" => "Chukyo",
            "京都" or "Kyoto" => "Kyoto",
            "阪神" or "Hanshin" => "Hanshin",
            "小倉" or "Kokura" => "Kokura",
            _ => string.Empty,
        };
        return canonical.Length > 0;
    }

    private static Dictionary<string, string> MergeAttributes(IEnumerable<Dictionary<string, string>> sources)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in sources)
            foreach (var pair in source)
                if (!string.IsNullOrWhiteSpace(pair.Value)) result[pair.Key] = pair.Value;
        return result;
    }

    private static string SerializeTaskMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return "{}";
        if (metadata.Count > 32)
            throw new ArgumentException("Task metadata cannot contain more than 32 keys.", nameof(metadata));
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        var totalLength = 0;
        foreach (var pair in metadata)
        {
            var key = pair.Key?.Trim() ?? string.Empty;
            var value = pair.Value?.Trim() ?? string.Empty;
            if (key.Length is < 1 or > 64 || value.Length > 2048)
                throw new ArgumentException("Task metadata key or value exceeds its limit.", nameof(metadata));
            if (!AllowedTaskMetadataKeys.Contains(key))
                throw new ArgumentException($"Task metadata key '{key}' is not supported.", nameof(metadata));
            if (key.Contains("password", StringComparison.OrdinalIgnoreCase)
                || key.Contains("cookie", StringComparison.OrdinalIgnoreCase)
                || key.Contains("authorization", StringComparison.OrdinalIgnoreCase)
                || key.Contains("token", StringComparison.OrdinalIgnoreCase)
                || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || key.Contains("html", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Task metadata key '{key}' is not allowed.", nameof(metadata));
            totalLength += key.Length + value.Length;
            if (totalLength > 8192)
                throw new ArgumentException("Task metadata exceeds the total size limit.", nameof(metadata));
            normalized[key] = value;
        }
        return JsonSerializer.Serialize(normalized);
    }

    private static IReadOnlyDictionary<string, string> DeserializeTaskMetadata(string json)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    private static void MergeLocation(ResourceLocationEntity target, ResourceLocationEntity source)
    {
        static int Rank(ResourceLocationStatus value) => value switch
        {
            ResourceLocationStatus.Active => 4,
            ResourceLocationStatus.Unknown => 3,
            ResourceLocationStatus.Suspect => 2,
            _ => 1,
        };
        if (Rank(source.Status) > Rank(target.Status)) target.Status = source.Status;
        if (source.DiscoveredAt < target.DiscoveredAt) target.DiscoveredAt = source.DiscoveredAt;
        if (source.LastVerifiedAt > target.LastVerifiedAt) target.LastVerifiedAt = source.LastVerifiedAt;
        if (source.LastFailedAt > target.LastFailedAt)
        {
            target.LastFailedAt = source.LastFailedAt;
            target.LastFailureCode = source.LastFailureCode;
        }
    }

    private CollectionPlatformDbContext CreateDbContext() => new(_dbOptions);

    private static Task<bool> IsResourceSuppressedAsync(CollectionPlatformDbContext db, long resourcePk,
        CancellationToken cancellationToken) =>
        (from resource in db.Resources
         join suppression in db.ResourceSuppressions
             on new { resource.Type, resource.Provider, resource.ResourceId }
             equals new { suppression.Type, suppression.Provider, suppression.ResourceId }
         where resource.ResourcePk == resourcePk
         select suppression).AnyAsync(cancellationToken);
}
