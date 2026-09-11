using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed class CollectionPlatformStore
{
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

            if (!string.IsNullOrWhiteSpace(batchId))
            {
                var existingRequest = await db.Requests.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.ResourcePk == resourceEntity.ResourcePk && x.DefinitionId == definition.Value
                    && x.BatchId == batchId, cancellationToken).ConfigureAwait(false);
                if (existingRequest is not null)
                {
                    var existingTask = await db.Tasks.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.RequestId == existingRequest.RequestId, cancellationToken)
                        .ConfigureAwait(false);
                    var existingActive = await db.ActiveTasks.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.ResourcePk == resourceEntity.ResourcePk && x.DefinitionId == definition.Value,
                        cancellationToken).ConfigureAwait(false);
                    await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return new(existingRequest.RequestId,
                        existingTask?.TaskId ?? existingActive?.TaskId ?? Guid.Empty, false);
                }
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
            var receipts = new List<CollectionRequestReceipt>(normalized.Count);
            var created = 0;
            foreach (var target in normalized)
            {
                var resource = await db.Resources.SingleOrDefaultAsync(x => x.Type == target.Resource.Type
                    && x.Provider == target.Resource.Provider && x.ResourceId == target.Resource.Id, cancellationToken);
                if (resource is null)
                {
                    resource = new CollectionResourceEntity
                    {
                        Type = target.Resource.Type, Provider = target.Resource.Provider,
                        ResourceId = target.Resource.Id, EffectiveDate = target.EffectiveDate,
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
                    RequestId = Guid.NewGuid(), ResourcePk = resource.ResourcePk, DefinitionId = definition.Value,
                    RequestedRevision = requestedRevision, Reason = reason, RequestedAt = requestedAt, BatchId = batchId,
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
                        TaskId = Guid.NewGuid(), RequestId = request.RequestId, ResourcePk = resource.ResourcePk,
                        DefinitionId = definition.Value, RequestedRevision = requestedRevision,
                        Status = CollectionTaskStatus.Ready, Lane = lane, Priority = priority,
                        AvailableAt = requestedAt, CreatedAt = requestedAt, UpdatedAt = requestedAt,
                        DispatchGeneration = 1,
                    };
                    taskId = task.TaskId;
                    db.Tasks.Add(task);
                    db.ActiveTasks.Add(new CollectionActiveTaskEntity
                        { ResourcePk = resource.ResourcePk, DefinitionId = definition.Value, TaskId = taskId });
                    db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
                    {
                        OutboxId = Guid.NewGuid(), TaskId = taskId, DispatchGeneration = 1,
                        AvailableAt = requestedAt, CreatedAt = requestedAt,
                    });
                    created++;
                }
                else taskId = active.TaskId;
                var state = await db.States.FirstOrDefaultAsync(x => x.ResourcePk == resource.ResourcePk
                    && x.DefinitionId == definition.Value, cancellationToken);
                if (state is null)
                    db.States.Add(new CollectionStateEntity
                    {
                        ResourcePk = resource.ResourcePk, DefinitionId = definition.Value,
                        RequiredRevision = requestedRevision, Status = CollectionStateStatus.Pending,
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
                .ThenByDescending(x => x.LastVerifiedAt).Select(x => new ResourceLocationCandidate(
                    x.LocationId, new Uri(x.Url), x.Source, x.Status, x.LastVerifiedAt)).ToList();
            if (Uri.TryCreate(request.ExplicitUrl, UriKind.Absolute, out var explicitUrl))
                candidates.Insert(0, new(0, explicitUrl, ResourceLocationSource.Explicit,
                    ResourceLocationStatus.Unknown, null));
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
                JsonSerializer.Deserialize<Dictionary<string, string>>(resource.AttributesJson) ?? [], candidates);
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
            if (completion.RequestedUrl is not null)
            {
                var requested = completion.RequestedUrl.AbsoluteUri;
                var location = await db.Locations.SingleOrDefaultAsync(x => x.ResourcePk == task.ResourcePk
                    && x.DefinitionId == task.DefinitionId && x.Url == requested, cancellationToken);
                if (location is null)
                {
                    location = new ResourceLocationEntity
                    {
                        ResourcePk = task.ResourcePk, DefinitionId = task.DefinitionId, Url = requested,
                        Source = ResourceLocationSource.Explicit, Status = ResourceLocationStatus.Unknown,
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
                        ResourcePk = task.ResourcePk, DefinitionId = task.DefinitionId, Url = redirected,
                        Source = ResourceLocationSource.Redirected, Status = ResourceLocationStatus.Active,
                        DiscoveredAt = now, LastVerifiedAt = now,
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
                state.Status = CollectionStateStatus.Unknown;
                db.ActiveTasks.Remove(await db.ActiveTasks.SingleAsync(x => x.TaskId == taskId, cancellationToken));
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
                            TaskId = Guid.NewGuid(), RequestId = followUpRequest.RequestId,
                            ResourcePk = task.ResourcePk, DefinitionId = task.DefinitionId,
                            RequestedRevision = state.RequiredRevision, Status = CollectionTaskStatus.Ready,
                            Lane = task.Lane, Priority = task.Priority, AvailableAt = now,
                            CreatedAt = now, UpdatedAt = now, DispatchGeneration = 1,
                        };
                        db.Tasks.Add(followUp);
                        db.ActiveTasks.Add(new CollectionActiveTaskEntity
                            { ResourcePk = task.ResourcePk, DefinitionId = task.DefinitionId, TaskId = followUp.TaskId });
                        db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
                        {
                            OutboxId = Guid.NewGuid(), TaskId = followUp.TaskId, DispatchGeneration = 1,
                            AvailableAt = now, CreatedAt = now,
                        });
                        state.Status = CollectionStateStatus.Pending;
                    }
                }
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
                QueueFailureNotification(db, task, completion.ErrorCode, completion.ErrorMessage, now);
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
        if (row?.LeaseExpiresAt is null || row.LeaseExpiresAt <= DateTimeOffset.UtcNow) return false;
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
        CollectionDefinitionId definition, CancellationToken cancellationToken = default)
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
        var requestRows = await db.Requests.AsNoTracking().Where(x => x.ResourcePk == item.ResourcePk
                && x.DefinitionId == definition.Value).ToListAsync(cancellationToken);
        var requests = requestRows.OrderByDescending(x => x.RequestedAt).Select(x =>
            new CollectionRequestSummary(x.RequestId, x.RequestedRevision, x.Reason, x.RequestedAt,
                x.ExplicitUrl, x.BatchId)).ToList();
        var taskRows = await db.Tasks.AsNoTracking().Where(x => x.ResourcePk == item.ResourcePk
                && x.DefinitionId == definition.Value).ToListAsync(cancellationToken);
        var tasks = taskRows.OrderByDescending(x => x.CreatedAt).Select(x => new CollectionTaskSummary(x.TaskId, resource, definition, x.Status,
            x.Lane, x.Priority, x.RequestedRevision, x.AvailableAt, x.AttemptCount)).ToList();
        var taskIds = tasks.Select(x => x.TaskId).ToArray();
        var attemptRows = await db.Attempts.AsNoTracking().Where(x => taskIds.Contains(x.TaskId))
            .ToListAsync(cancellationToken);
        var attempts = attemptRows.OrderByDescending(x => x.StartedAt).Select(x => new CollectionAttemptSummary(x.AttemptId, x.TaskId,
                x.AttemptNumber, x.StartedAt, x.FinishedAt, x.Result, x.ErrorCode, x.ErrorMessage,
                x.RequestedUrl, x.FinalUrl, x.HttpStatusCode, x.PageIdentification)).ToList();
        var state = stateEntity is null ? null : new CollectionStateSnapshot(resource, definition,
            stateEntity.AppliedRevision, stateEntity.RequiredRevision, stateEntity.LastCollectedAt,
            stateEntity.NextCollectionAt, stateEntity.Status);
        return new(state, locations, requests, tasks, attempts);
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
        if (await db.Controls.AsNoTracking().AnyAsync(x => x.ControlId == "pipeline" && x.IsPaused, cancellationToken))
            return [];
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

    public async Task<CollectionTaskPage> SearchTasksAsync(CollectionTaskQuery request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        await using var db = CreateDbContext();
        var query = from task in db.Tasks.AsNoTracking()
            join resource in db.Resources.AsNoTracking() on task.ResourcePk equals resource.ResourcePk
            select new { task, resource };

        if (request.Statuses is { Count: > 0 })
        {
            var statuses = request.Statuses.Distinct().ToArray();
            query = query.Where(x => statuses.Contains(x.task.Status));
        }
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
        if (request.CreatedFrom.HasValue || request.CreatedTo.HasValue)
        {
            // The SQLite provider cannot translate DateTimeOffset comparisons. Apply only this optional
            // administration filter in memory after all selective SQL predicates have run.
            var candidates = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
            var filtered = candidates.Where(x => (!request.CreatedFrom.HasValue
                                                   || x.task.CreatedAt >= request.CreatedFrom.Value)
                                                  && (!request.CreatedTo.HasValue
                                                      || x.task.CreatedAt <= request.CreatedTo.Value))
                .OrderByDescending(x => x.task.Priority).ThenBy(x => x.task.TaskId).ToList();
            return new(filtered.Count, page, pageSize, filtered.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new CollectionTaskSummary(x.task.TaskId,
                    new ResourceKey(x.resource.Type, x.resource.Provider, x.resource.ResourceId),
                    new CollectionDefinitionId(x.task.DefinitionId), x.task.Status, x.task.Lane, x.task.Priority,
                    x.task.RequestedRevision, x.task.AvailableAt, x.task.AttemptCount)).ToList());
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        // SQLite cannot order DateTimeOffset columns. TaskId is used as the stable tie-breaker;
        // priority remains the primary operational ordering for the administration list.
        var rows = await query.OrderByDescending(x => x.task.Priority).ThenBy(x => x.task.TaskId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Select(x => new CollectionTaskSummary(x.task.TaskId,
            new ResourceKey(x.resource.Type, x.resource.Provider, x.resource.ResourceId),
            new CollectionDefinitionId(x.task.DefinitionId), x.task.Status, x.task.Lane, x.task.Priority,
            x.task.RequestedRevision, x.task.AvailableAt, x.task.AttemptCount)).ToList();
        return new(totalCount, page, pageSize, items);
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
            QueueFailureNotification(db, task, "DeadLetterQueue", errorMessage, now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<CollectionWatchdogResult> RunWatchdogAsync(DateTimeOffset now, int maxDispatchAttempts,
        TimeSpan dispatchGrace, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var runningBefore = await db.Tasks.CountAsync(x => x.Status == CollectionTaskStatus.Running, cancellationToken);
            await ReclaimExpiredAsync(db, now, cancellationToken).ConfigureAwait(false);
            var reclaimed = runningBefore - await db.Tasks.CountAsync(x => x.Status == CollectionTaskStatus.Running, cancellationToken);
            var tasks = await db.Tasks.Where(x => x.Status == CollectionTaskStatus.Ready).ToListAsync(cancellationToken);
            var redispatched = 0;
            var deadLettered = 0;
            foreach (var task in tasks.Where(x => x.UpdatedAt <= now.Subtract(dispatchGrace)))
            {
                if (await db.DispatchOutbox.AnyAsync(x => x.TaskId == task.TaskId && x.DispatchedAt == null, cancellationToken))
                    continue;
                if (task.DispatchGeneration >= maxDispatchAttempts)
                {
                    task.Status = CollectionTaskStatus.DeadLetter;
                    task.FinishedAt = now;
                    var active = await db.ActiveTasks.SingleOrDefaultAsync(x => x.TaskId == task.TaskId, cancellationToken);
                    if (active is not null) db.ActiveTasks.Remove(active);
                    var state = await db.States.SingleAsync(x => x.ResourcePk == task.ResourcePk
                        && x.DefinitionId == task.DefinitionId, cancellationToken);
                    state.Status = CollectionStateStatus.Failed;
                    state.UpdatedAt = now;
                    QueueFailureNotification(db, task, "DispatchAttemptsExceeded", null, now);
                    deadLettered++;
                }
                else
                {
                    task.DispatchGeneration++;
                    task.UpdatedAt = now;
                    db.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
                    {
                        OutboxId = Guid.NewGuid(), TaskId = task.TaskId,
                        DispatchGeneration = task.DispatchGeneration, AvailableAt = now, CreatedAt = now
                    });
                    redispatched++;
                }
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(reclaimed, redispatched, deadLettered);
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
                    BatchId = batchId, Provider = provider, From = from, To = to, CreatedAt = now
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
        var holes = rows.Where(x => x.task.Status is CollectionTaskStatus.Failed or CollectionTaskStatus.DeadLetter)
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
                        Type = seed.Resource.Type, Provider = seed.Resource.Provider,
                        ResourceId = seed.Resource.Id, EffectiveDate = seed.EffectiveDate,
                        AttributesJson = JsonSerializer.Serialize(seed.Attributes), CreatedAt = seed.CollectedAt
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
                        ResourcePk = resource.ResourcePk, DefinitionId = seed.Definition.Value,
                        AppliedRevision = seed.AppliedRevision, RequiredRevision = seed.AppliedRevision,
                        LastCollectedAt = seed.CollectedAt, Status = CollectionStateStatus.Current,
                        UpdatedAt = seed.CollectedAt
                    });
                }
                if (seed.SourceUrl is not null && !await db.Locations.AnyAsync(x => x.ResourcePk == resource.ResourcePk
                    && x.DefinitionId == seed.Definition.Value && x.Url == seed.SourceUrl.AbsoluteUri, cancellationToken))
                {
                    locationsAdded++;
                    if (!dryRun) db.Locations.Add(new ResourceLocationEntity
                    {
                        ResourcePk = resource.ResourcePk, DefinitionId = seed.Definition.Value,
                        Url = seed.SourceUrl.AbsoluteUri, Source = ResourceLocationSource.Discovered,
                        Status = ResourceLocationStatus.Active, DiscoveredAt = seed.CollectedAt,
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

    private static void QueueFailureNotification(CollectionPlatformDbContext db, CollectionTaskEntity task,
        string? errorCode, string? errorMessage, DateTimeOffset now)
        => db.FailureNotifications.Add(new CollectionFailureNotificationEntity
        {
            NotificationId = Guid.NewGuid(), TaskId = task.TaskId, Status = task.Status.ToString(),
            ErrorCode = errorCode, ErrorMessage = errorMessage, AttemptCount = task.AttemptCount,
            FailedAt = now, AvailableAt = now
        });

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
                state.Status = CollectionStateStatus.Unknown;
                state.UpdatedAt = now;
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
                OutboxId = Guid.NewGuid(), TaskId = task.TaskId, DispatchGeneration = task.DispatchGeneration,
                AvailableAt = now, CreatedAt = now,
            });
        }
        if (expired.Count > 0) await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private CollectionPlatformDbContext CreateDbContext() => new(_dbOptions);
}
