using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class ProcessingStateStore : IProcessingStateStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DbContextOptions<ProcessingStateDbContext> _dbContextOptions;
    private readonly AgentProcessingOptions _options;
    private readonly ILogger<ProcessingStateStore> _logger;
    private readonly string _dbPath;
    private readonly string _stateDirectory;

    public ProcessingStateStore(IOptions<AgentProcessingOptions> options, ILogger<ProcessingStateStore> logger)
    {
        _options = options.Value;

        var dir = _options.StateDirectory;
        var stateDirectory = string.IsNullOrWhiteSpace(dir)
            ? Path.Combine(AppContext.BaseDirectory, "agent-processing-state")
            : dir;
        _stateDirectory = Path.GetFullPath(stateDirectory);

        Directory.CreateDirectory(stateDirectory);

        var jobStoreFileName = string.IsNullOrWhiteSpace(_options.JobStoreFileName)
            ? "processing-jobs.db"
            : _options.JobStoreFileName;
        var dbPath = Path.Combine(stateDirectory, jobStoreFileName);
        _dbPath = Path.GetFullPath(dbPath);

        _dbContextOptions = new DbContextOptionsBuilder<ProcessingStateDbContext>()
            .UseSqlite($"Data Source={dbPath};Pooling=False")
            .Options;

        _logger = logger;
        InitializeDatabase();
    }

    public async Task EnqueuePredictionCandidatesAsync(
        IEnumerable<string> raceIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        foreach (var raceId in raceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            await ScheduleJobAsync(
                AgentJobType.PredictionExecution,
                raceId,
                raceId,
                now,
                priority: 100,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<string>> TakeReadyPredictionCandidatesAsync(
        DateTimeOffset now,
        TimeSpan minAge,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        var leaseDuration = TimeSpan.FromMinutes(Math.Max(1, _options.PredictionLeaseMinutes));
        var jobs = await AcquireReadyJobsAsync(
            AgentJobType.PredictionExecution,
            now,
            minAge,
            Math.Max(1, maxCount),
            leaseDuration,
            cancellationToken).ConfigureAwait(false);

        return jobs.Select(x => x.Payload).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()!;
    }

    public async Task MarkPredictionCompletedAsync(string raceId, CancellationToken cancellationToken = default)
    {
        await CompleteJobAsync(AgentJobType.PredictionExecution, raceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task RequeuePredictionCandidateAsync(
        string raceId,
        DateTimeOffset now,
        string error,
        CancellationToken cancellationToken = default)
    {
        await RequeueJobAsync(AgentJobType.PredictionExecution, raceId, now, error, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasMarkerAsync(
        string markerType,
        string markerKey,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            return await dbContext.Markers
                .AnyAsync(
                    x => x.MarkerType == markerType && x.MarkerKey == markerKey,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkMarkerAsync(
        string markerType,
        string markerKey,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var exists = await dbContext.Markers
                .AnyAsync(
                    x => x.MarkerType == markerType && x.MarkerKey == markerKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (exists)
            {
                return;
            }

            dbContext.Markers.Add(new ProcessingMarkerEntity
            {
                MarkerType = markerType,
                MarkerKey = markerKey,
                CreatedAt = DateTimeOffset.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnqueueJobAsync(
        string jobType,
        string deduplicationKey,
        string payload,
        DateTimeOffset now,
        int priority = 0,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var exists = await dbContext.Jobs
                .AnyAsync(
                    x => x.JobType == jobType && x.DeduplicationKey == deduplicationKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (exists)
            {
                return;
            }

            var job = new ProcessingJobEntity
            {
                JobId = BuildJobId(jobType, deduplicationKey),
                JobType = jobType,
                DeduplicationKey = deduplicationKey,
                Payload = payload,
                Status = AgentJobStatus.Ready,
                Priority = priority,
                FirstQueuedAt = now,
                AvailableAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.Jobs.Add(job);
            QueueDispatch(dbContext, job, now);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ジョブ投入に失敗しました。JobType={JobType} DeduplicationKey={DeduplicationKey}", jobType, deduplicationKey);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ScheduleJobAsync(
        string jobType,
        string deduplicationKey,
        string payload,
        DateTimeOffset now,
        int priority = 0,
        string? parentJobId = null,
        JobRelationType parentRelationType = JobRelationType.AggregatedBy,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var job = await dbContext.Jobs
                .SingleOrDefaultAsync(
                    x => x.JobType == jobType && x.DeduplicationKey == deduplicationKey,
                    cancellationToken)
                .ConfigureAwait(false);

            if (job is null)
            {
                var newJob = new ProcessingJobEntity
                {
                    JobId = BuildJobId(jobType, deduplicationKey),
                    JobType = jobType,
                    DeduplicationKey = deduplicationKey,
                    Payload = payload,
                    ParentJobId = parentJobId,
                    ParentRelationType = parentRelationType,
                    Status = AgentJobStatus.Ready,
                    Priority = priority,
                    FirstQueuedAt = now,
                    AvailableAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                dbContext.Jobs.Add(newJob);
                QueueDispatch(dbContext, newJob, now);

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            job.Payload = payload;
            job.Priority = priority;
            if (job.ParentJobId is null && parentJobId is not null)
            {
                job.ParentJobId = parentJobId;
                job.ParentRelationType = parentRelationType;
            }
            job.UpdatedAt = now;

            // 親ジョブの再実行時も、完了済みの子タスクは再投入しない。
            if (job.Status == AgentJobStatus.Succeeded
                && !string.IsNullOrWhiteSpace(parentJobId)
                && string.Equals(job.ParentJobId, parentJobId, StringComparison.Ordinal))
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (job.Status is AgentJobStatus.Succeeded or AgentJobStatus.Cancelled or AgentJobStatus.DeadLetter)
            {
                job.Status = AgentJobStatus.Ready;
                job.AvailableAt = now;
                job.StartedAt = null;
                job.LeaseExpiresAt = null;
                job.LastError = null;
                QueueDispatch(dbContext, job, now);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AcquiredProcessingJob>> AcquireReadyJobsAsync(
        string jobType,
        DateTimeOffset now,
        TimeSpan minAge,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await ReclaimExpiredLeasesAsync(dbContext, now, cancellationToken).ConfigureAwait(false);

            var activeRunningCount = await dbContext.Jobs
                .CountAsync(x => x.Status == AgentJobStatus.Running, cancellationToken)
                .ConfigureAwait(false);
            var concurrencyLimit = Math.Max(1, _options.MaxConcurrentJobs);
            var availableSlots = concurrencyLimit - activeRunningCount;
            if (availableSlots <= 0)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return [];
            }

            var readyJobCandidates = await dbContext.Jobs
                .Where(x => x.JobType == jobType
                    && x.Status == AgentJobStatus.Ready)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var readyJobs = readyJobCandidates
                .Where(x => x.AvailableAt <= now && x.FirstQueuedAt <= now.Subtract(minAge))
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.AvailableAt)
                .ThenBy(x => x.FirstQueuedAt)
                .Take(Math.Max(1, Math.Min(maxCount, availableSlots)))
                .ToList();

            if (readyJobs.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return [];
            }

            foreach (var job in readyJobs)
            {
                var leaseToken = Guid.NewGuid().ToString("N");
                job.Status = AgentJobStatus.Running;
                job.StartedAt = now;
                job.LeaseExpiresAt = now.Add(leaseDuration);
                job.LeaseToken = leaseToken;
                job.UpdatedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return readyJobs
                .Select(x => new AcquiredProcessingJob(x.JobId, x.DeduplicationKey, x.Payload))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteJobAsync(
        string jobType,
        string deduplicationKey,
        CancellationToken cancellationToken = default)
    {
        await UpdateJobStatusAsync(
            jobType,
            deduplicationKey,
            AgentJobStatus.Succeeded,
            null,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task FailJobAsync(
        string jobType,
        string deduplicationKey,
        string? error,
        CancellationToken cancellationToken = default)
    {
        await UpdateJobStatusAsync(
            jobType,
            deduplicationKey,
            AgentJobStatus.Failed,
            null,
            error,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<LeasedCollectionTask?> AcquireCollectionTaskAsync(
        string jobType,
        string deduplicationKey,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
        => AcquireCollectionTaskAsync(jobType, deduplicationKey, -1, now, leaseDuration, cancellationToken);

    public async Task<LeasedCollectionTask?> AcquireCollectionTaskAsync(
        string jobType,
        string deduplicationKey,
        long dispatchGeneration,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            await ReclaimExpiredLeasesAsync(dbContext, now, cancellationToken).ConfigureAwait(false);
            var job = await dbContext.Jobs.SingleOrDefaultAsync(
                x => x.JobType == jobType && x.DeduplicationKey == deduplicationKey,
                cancellationToken).ConfigureAwait(false);
            if (job is null || job.Status != AgentJobStatus.Ready || job.AvailableAt > now
                || (dispatchGeneration >= 0 && job.DispatchGeneration != dispatchGeneration))
                return null;

            var leaseToken = Guid.NewGuid().ToString("N");
            var leaseExpiresAt = now.Add(leaseDuration);
            job.Status = AgentJobStatus.Running;
            job.StartedAt = now;
            job.LeaseExpiresAt = leaseExpiresAt;
            job.LeaseToken = leaseToken;
            job.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new LeasedCollectionTask(job.JobId, job.JobType, job.DeduplicationKey, job.Payload, leaseToken, leaseExpiresAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<bool> CompleteCollectionTaskAsync(
        string jobType,
        string deduplicationKey,
        string leaseToken,
        CancellationToken cancellationToken = default)
        => UpdateLeasedCollectionTaskAsync(jobType, deduplicationKey, leaseToken, AgentJobStatus.Succeeded, null, null, cancellationToken);

    public Task<bool> FailCollectionTaskAsync(
        string jobType,
        string deduplicationKey,
        string leaseToken,
        string? error,
        CancellationToken cancellationToken = default)
        => UpdateLeasedCollectionTaskAsync(jobType, deduplicationKey, leaseToken, AgentJobStatus.Failed, null, error, cancellationToken);

    public Task<bool> RequeueCollectionTaskAsync(
        string jobType,
        string deduplicationKey,
        string leaseToken,
        DateTimeOffset availableAt,
        string? error,
        CancellationToken cancellationToken = default)
        => UpdateLeasedCollectionTaskAsync(jobType, deduplicationKey, leaseToken, AgentJobStatus.Ready, availableAt, error, cancellationToken);

    public async Task<IReadOnlyList<PendingCollectionTaskDispatch>> GetPendingCollectionTaskDispatchesAsync(
        DateTimeOffset now,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var pendingCandidates = await dbContext.DispatchOutbox
                .Where(x => x.DispatchedAt == null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var jobIds = pendingCandidates.Select(x => x.TaskId).Distinct(StringComparer.Ordinal).ToList();
            var jobsById = await dbContext.Jobs
                .Where(x => jobIds.Contains(x.JobId))
                .ToDictionaryAsync(x => x.JobId, x => x, cancellationToken)
                .ConfigureAwait(false);
            var entities = pendingCandidates
                .Where(x => x.AvailableAt <= now)
                .OrderByDescending(x => jobsById.TryGetValue(x.TaskId, out var job) ? job.Priority : 0)
                .ThenBy(x => x.AvailableAt)
                .ThenBy(x => x.CreatedAt)
                .ThenBy(x => x.TaskId)
                .Take(Math.Max(1, maxCount))
                .ToList();
            return entities.Select(x => new PendingCollectionTaskDispatch(
                x.OutboxId,
                new CollectionTaskNotification(x.TaskId, x.JobType, x.DeduplicationKey, x.DispatchGeneration),
                x.AttemptCount)).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkCollectionTaskDispatchedAsync(string outboxId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var dispatch = await dbContext.DispatchOutbox.FindAsync([outboxId], cancellationToken).ConfigureAwait(false);
            if (dispatch is null) return;
            dispatch.DispatchedAt = now;
            dispatch.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task MarkCollectionTaskDispatchFailedAsync(string outboxId, DateTimeOffset now, string error, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var dispatch = await dbContext.DispatchOutbox.FindAsync([outboxId], cancellationToken).ConfigureAwait(false);
            if (dispatch is null) return;
            dispatch.AttemptCount += 1;
            dispatch.LastError = error;
            dispatch.AvailableAt = now.AddMinutes(Math.Min(15, Math.Max(1, dispatch.AttemptCount)));
            dispatch.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<int> RequeueReadyCollectionDispatchesAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var readyJobs = (await dbContext.Jobs
                .Where(x => x.Status == AgentJobStatus.Ready)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.AvailableAt)
                .ThenBy(x => x.CreatedAt)
                .ThenBy(x => x.JobId)
                .ToList();
            var pendingJobIds = (await dbContext.DispatchOutbox
                    .Where(x => x.DispatchedAt == null)
                    .Select(x => x.TaskId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToHashSet(StringComparer.Ordinal);

            var count = 0;
            foreach (var job in readyJobs.Where(x => CollectionDispatchPolicy.IsDispatchable(x.JobType) && !pendingJobIds.Contains(x.JobId)))
            {
                QueueDispatch(dbContext, job, now > job.AvailableAt ? now : job.AvailableAt);
                count++;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return count;
        }
        finally { _gate.Release(); }
    }

    public async Task<CollectionResetPreview> GetResetPreviewAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var statuses = await dbContext.Jobs.GroupBy(x => x.Status)
                .Select(x => new { Status = x.Key, Count = x.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var pendingOutbox = await dbContext.DispatchOutbox.CountAsync(x => x.DispatchedAt == null, cancellationToken)
                .ConfigureAwait(false);
            var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var names = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) names.Add(reader.GetString(0));
            }
            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                await using var count = connection.CreateCommand();
                count.CommandText = $"SELECT COUNT(*) FROM \"{name.Replace("\"", "\"\"")}\";";
                counts[name] = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            }
            return new CollectionResetPreview(statuses.ToDictionary(x => x.Status, x => x.Count), pendingOutbox, counts);
        }
        finally { _gate.Release(); }
    }

    public async Task<int> CancelAllActiveJobsAsync(string actorId, string reason, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var jobs = await dbContext.Jobs
                .Where(x => x.Status != AgentJobStatus.Succeeded && x.Status != AgentJobStatus.Cancelled)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var job in jobs)
            {
                var previous = job.Status;
                job.Status = AgentJobStatus.Cancelled;
                job.DispatchGeneration += 1;
                job.LeaseToken = null;
                job.LeaseExpiresAt = null;
                job.StartedAt = null;
                job.UpdatedAt = now;
                dbContext.JobOperationAudits.Add(new JobOperationAuditEntity
                {
                    AuditId = Guid.NewGuid().ToString("N"),
                    JobId = job.JobId,
                    Operation = "QueueReset",
                    PreviousStatus = previous,
                    NewStatus = AgentJobStatus.Cancelled,
                    ActorId = actorId,
                    Reason = reason,
                    CreatedAt = now
                });
            }
            var pending = await dbContext.DispatchOutbox.Where(x => x.DispatchedAt == null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var dispatch in pending)
            {
                dispatch.DispatchedAt = now;
                dispatch.LastError = "Invalidated by queue reset.";
                dispatch.UpdatedAt = now;
            }
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return jobs.Count;
        }
        finally { _gate.Release(); }
    }

    public async Task<string> BackupAndResetAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        var backupPath = await BackupDatabaseAsync(backupDirectory, cancellationToken).ConfigureAwait(false);
        await ResetDatabaseAsync(cancellationToken).ConfigureAwait(false);
        return backupPath;
    }

    public async Task<string> BackupDatabaseAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureSafeDatabasePath(_dbPath, _stateDirectory);
            Directory.CreateDirectory(backupDirectory);
            var backupPath = Path.Combine(backupDirectory, Path.GetFileName(_dbPath));
            await using (var source = new SqliteConnection($"Data Source={_dbPath};Pooling=False"))
            await using (var destination = new SqliteConnection($"Data Source={backupPath};Pooling=False"))
            {
                await source.OpenAsync(cancellationToken).ConfigureAwait(false);
                await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
                source.BackupDatabase(destination);
                await VerifySqliteAsync(source, cancellationToken).ConfigureAwait(false);
                await VerifySqliteAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            return backupPath;
        }
        finally { _gate.Release(); }
    }

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureSafeDatabasePath(_dbPath, _stateDirectory);
            SqliteConnection.ClearAllPools();
            await using (var dbContext = CreateDbContext())
            {
                await dbContext.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
                await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            }
            InitializeDatabase();
        }
        finally { _gate.Release(); }
    }

    private static void EnsureSafeDatabasePath(string databasePath, string allowedDirectory)
    {
        var root = Path.GetPathRoot(allowedDirectory);
        if (string.Equals(allowedDirectory.TrimEnd(Path.DirectorySeparatorChar), root?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The configured state directory is too broad for a destructive reset.");
        var relative = Path.GetRelativePath(allowedDirectory, databasePath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("The task database is outside the configured state directory.");
    }

    private static async Task VerifySqliteAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SQLite integrity check failed: {result}");
    }

    public async Task WaitForDependenciesAsync(
        string jobType,
        string deduplicationKey,
        CancellationToken cancellationToken = default)
    {
        await UpdateJobStatusAsync(
            jobType,
            deduplicationKey,
            AgentJobStatus.WaitingDependency,
            null,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingJobFailureNotification>> GetPendingJobFailureNotificationsAsync(
        DateTimeOffset now,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var candidates = await dbContext.JobFailureNotifications
                .Where(x => x.PublishedAt == null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return candidates
                .Where(x => x.AvailableAt <= now)
                .OrderBy(x => x.AvailableAt)
                .ThenBy(x => x.CreatedAt)
                .Take(Math.Max(1, maxCount))
                .Select(x => new PendingJobFailureNotification(
                    x.NotificationId,
                    x.JobId,
                    x.JobType,
                    x.DeduplicationKey,
                    x.Status,
                    x.Error,
                    x.AttemptCount,
                    x.FailedAt,
                    x.PublishAttemptCount))
                .ToList();
        }
        finally { _gate.Release(); }
    }

    public async Task MarkJobFailureNotificationPublishedAsync(
        string notificationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var notification = await dbContext.JobFailureNotifications
                .FindAsync([notificationId], cancellationToken).ConfigureAwait(false);
            if (notification is null) return;
            notification.PublishedAt = now;
            notification.LastPublishError = null;
            notification.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task MarkJobFailureNotificationPublishFailedAsync(
        string notificationId,
        DateTimeOffset now,
        string error,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var notification = await dbContext.JobFailureNotifications
                .FindAsync([notificationId], cancellationToken).ConfigureAwait(false);
            if (notification is null) return;
            notification.PublishAttemptCount += 1;
            notification.LastPublishError = error;
            notification.AvailableAt = now.AddMinutes(Math.Min(30, Math.Max(1, notification.PublishAttemptCount)));
            notification.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task RequeueJobAsync(
        string jobType,
        string deduplicationKey,
        DateTimeOffset now,
        string? error,
        CancellationToken cancellationToken = default,
        DateTimeOffset? availableAt = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var job = await dbContext.Jobs
                .SingleOrDefaultAsync(
                    x => x.JobType == jobType && x.DeduplicationKey == deduplicationKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (job is null)
            {
                return;
            }

            var scheduledAt = availableAt ?? now;
            job.Status = AgentJobStatus.Ready;
            job.AvailableAt = scheduledAt;
            job.LeaseExpiresAt = null;
            job.LeaseToken = null;
            job.LastError = error;
            job.UpdatedAt = now;
            QueueDispatch(dbContext, job, scheduledAt);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ForceRequeueJobAsync(
        string jobType,
        string deduplicationKey,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var job = await dbContext.Jobs
                .SingleOrDefaultAsync(
                    x => x.JobType == jobType && x.DeduplicationKey == deduplicationKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (job is null)
            {
                return false;
            }

            job.Status = AgentJobStatus.Ready;
            job.AvailableAt = now;
            job.StartedAt = null;
            job.LeaseExpiresAt = null;
            job.LeaseToken = null;
            job.LastError = null;
            job.UpdatedAt = now;
            QueueDispatch(dbContext, job, now);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RequeueRunningJobsAsync(
        IEnumerable<string> jobTypes,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var jobTypeSet = jobTypes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        if (jobTypeSet.Count == 0)
        {
            return 0;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var runningJobs = await dbContext.Jobs
                .Where(x => x.Status == AgentJobStatus.Running && jobTypeSet.Contains(x.JobType))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (runningJobs.Count == 0)
            {
                return 0;
            }

            foreach (var job in runningJobs)
            {
                job.Status = AgentJobStatus.Ready;
                job.StartedAt = null;
                job.LeaseExpiresAt = null;
                job.LeaseToken = null;
                job.LastError = null;
                job.AvailableAt = now;
                job.UpdatedAt = now;
                QueueDispatch(dbContext, job, now);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return runningJobs.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetActiveJobPayloadsAsync(
        string jobType,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            return await dbContext.Jobs
                .Where(x => x.JobType == jobType
                    && (x.Status == AgentJobStatus.Ready
                        || x.Status == AgentJobStatus.Running
                        || x.Status == AgentJobStatus.WaitingDependency
                        || x.Status == AgentJobStatus.Pending))
                .Select(x => x.Payload)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> GetAttemptCountAsync(
        string jobType,
        string deduplicationKey,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var attemptCount = await dbContext.Jobs
                .Where(x => x.JobType == jobType && x.DeduplicationKey == deduplicationKey)
                .Select(x => (int?)x.AttemptCount)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            return attemptCount ?? 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkJobAsDeadLetterAsync(
        string jobType,
        string deduplicationKey,
        string? error,
        CancellationToken cancellationToken = default)
    {
        await UpdateJobStatusAsync(
            jobType,
            deduplicationKey,
            AgentJobStatus.DeadLetter,
            null,
            error,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertRaceCardCollectionStatusAsync(
        DateOnly raceDate,
        string racecourse,
        int raceNumber,
        string? raceId,
        string? raceName,
        string? sourceUrl,
        RaceDataCollectionState status,
        RaceDataCollectionErrorCode? errorCode,
        string? errorReason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await UpsertRaceDataCollectionStatusAsync(
            raceDate,
            racecourse,
            raceNumber,
            raceId,
            raceName,
            sourceUrl,
            status,
            errorCode,
            errorReason,
            now,
            isRaceCard: true,
            raceResultOrigin: null,
            requestedByRaceId: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertRaceResultCollectionStatusAsync(
        DateOnly raceDate,
        string racecourse,
        int raceNumber,
        string? raceId,
        string? raceName,
        string? sourceUrl,
        RaceDataCollectionState status,
        RaceResultAcquisitionOrigin origin,
        string? requestedByRaceId,
        RaceDataCollectionErrorCode? errorCode,
        string? errorReason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await UpsertRaceDataCollectionStatusAsync(
            raceDate,
            racecourse,
            raceNumber,
            raceId,
            raceName,
            sourceUrl,
            status,
            errorCode,
            errorReason,
            now,
            isRaceCard: false,
            raceResultOrigin: origin,
            requestedByRaceId: requestedByRaceId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RaceDataCollectionStatusReadModel>> GetRaceDataCollectionStatusesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            return await dbContext.RaceDataCollectionStatuses
                .Where(x => x.RaceDate >= from && x.RaceDate <= to)
                .OrderBy(x => x.RaceDate)
                .ThenBy(x => x.Racecourse)
                .ThenBy(x => x.RaceNumber)
                .Select(x => new RaceDataCollectionStatusReadModel(
                    x.RaceDate,
                    x.Racecourse,
                    x.RaceNumber,
                    x.RaceId,
                    x.RaceName,
                    x.RaceCardUrl,
                    x.RaceCardStatus,
                    x.RaceCardErrorCode,
                    x.RaceCardErrorReason,
                    x.RaceCardUpdatedAt,
                    x.RaceResultUrl,
                    x.RaceResultStatus,
                    x.RaceResultOrigin,
                    x.RequestedByRaceId,
                    x.RaceResultErrorCode,
                    x.RaceResultErrorReason,
                    x.RaceResultUpdatedAt,
                    x.UpdatedAt))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAgentAcquisitionStatusAsync(
        string acquisitionKey,
        AgentAcquisitionSubjectType subjectType,
        AgentAcquisitionOperationType operationType,
        string? providerType,
        string? subjectId,
        string subjectName,
        string? relatedRaceId,
        string? originJobId,
        string? sourceUrl,
        RaceDataCollectionState status,
        RaceDataCollectionErrorCode? errorCode,
        string? errorReason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var entity = await dbContext.AgentAcquisitionStatuses
                .SingleOrDefaultAsync(x => x.AcquisitionKey == acquisitionKey, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                entity = new AgentAcquisitionStatusEntity
                {
                    AcquisitionKey = acquisitionKey,
                    CreatedAt = now,
                };
                dbContext.AgentAcquisitionStatuses.Add(entity);
            }

            entity.SubjectType = subjectType;
            entity.OperationType = operationType;
            entity.ProviderType = providerType;
            entity.SubjectId = string.IsNullOrWhiteSpace(subjectId) ? entity.SubjectId : subjectId;
            entity.SubjectName = subjectName;
            entity.RelatedRaceId = string.IsNullOrWhiteSpace(relatedRaceId) ? entity.RelatedRaceId : relatedRaceId;
            entity.OriginJobId = string.IsNullOrWhiteSpace(originJobId) ? entity.OriginJobId : originJobId;
            entity.SourceUrl = string.IsNullOrWhiteSpace(sourceUrl) ? entity.SourceUrl : sourceUrl;
            entity.Status = status;
            entity.ErrorCode = errorCode;
            entity.ErrorReason = errorReason;
            entity.UpdatedAt = now;

            dbContext.AgentAcquisitionHistory.Add(new AgentAcquisitionHistoryEntity
            {
                AcquisitionKey = acquisitionKey,
                ProviderType = providerType,
                Status = status,
                ErrorCode = errorCode,
                ErrorReason = errorReason,
                OriginJobId = originJobId,
                SourceUrl = sourceUrl,
                OccurredAt = now,
            });

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentAcquisitionStatusReadModel>> GetAgentAcquisitionStatusesAsync(
        DateOnly from,
        DateOnly to,
        AgentAcquisitionSubjectType? subjectType,
        RaceDataCollectionState? status,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var fromInclusive = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var toExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            var items = await dbContext.AgentAcquisitionStatuses
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return items
                .Where(x => x.UpdatedAt >= fromInclusive && x.UpdatedAt < toExclusive)
                .Where(x => !subjectType.HasValue || x.SubjectType == subjectType.Value)
                .Where(x => !status.HasValue || x.Status == status.Value)
                .OrderByDescending(x => x.UpdatedAt)
                .Select(x => new AgentAcquisitionStatusReadModel(
                    x.AcquisitionKey,
                    x.SubjectType,
                    x.OperationType,
                    x.ProviderType,
                    x.SubjectId,
                    x.SubjectName,
                    x.RelatedRaceId,
                    x.OriginJobId,
                    x.SourceUrl,
                    x.Status,
                    x.ErrorCode,
                    x.ErrorReason,
                    x.UpdatedAt,
                    x.CreatedAt))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentJobStatusReadModel>> GetJobStatusesAsync(
        string? jobType,
        AgentJobStatus? status,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var items = await dbContext.Jobs
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return items
                .Where(x => string.IsNullOrWhiteSpace(jobType) || string.Equals(x.JobType, jobType, StringComparison.Ordinal))
                .Where(x => !status.HasValue || x.Status == status.Value)
                .OrderByDescending(x => x.UpdatedAt)
                .Take(Math.Max(1, limit))
                .Select(x => new AgentJobStatusReadModel(
                    BuildJobId(x.JobType, x.DeduplicationKey),
                    x.JobType,
                    x.DeduplicationKey,
                    x.Status,
                    x.Priority,
                    x.AttemptCount,
                    x.FirstQueuedAt,
                    x.AvailableAt,
                    x.StartedAt,
                    x.LeaseExpiresAt,
                    x.LastError,
                    x.UpdatedAt))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentJobDetailReadModel?> GetJobDetailAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var entity = await dbContext.Jobs
                .SingleOrDefaultAsync(x => x.JobId == jobId, cancellationToken)
                .ConfigureAwait(false);
            if (entity is null) return null;
            var relatedEntities = await dbContext.Jobs.AsNoTracking()
                .Where(x => x.JobId == entity.ParentJobId || x.ParentJobId == entity.JobId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var parent = relatedEntities.Where(x => x.JobId == entity.ParentJobId)
                .Select(ToRelatedJob).SingleOrDefault();
            var children = relatedEntities.Where(x => x.ParentJobId == entity.JobId)
                .OrderByDescending(x => x.UpdatedAt).Select(ToRelatedJob).ToList();
            var auditEntities = await dbContext.JobOperationAudits.AsNoTracking()
                .Where(x => x.JobId == entity.JobId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var audits = auditEntities.OrderByDescending(x => x.CreatedAt)
                .Select(x => new JobOperationAuditReadModel(x.AuditId, x.Operation, x.PreviousStatus, x.NewStatus, x.ActorId, x.Reason, x.CreatedAt)).ToList();
            var attempts = await dbContext.JobAttempts.AsNoTracking().Where(x => x.JobId == entity.JobId)
                .OrderByDescending(x => x.AttemptNumber)
                .Select(x => new JobAttemptReadModel(x.AttemptNumber, x.Status, x.Error, x.StartedAt, x.CompletedAt)).ToListAsync(cancellationToken).ConfigureAwait(false);
            return new AgentJobDetailReadModel(
                entity.JobId,
                entity.JobType,
                entity.DeduplicationKey,
                entity.Payload,
                entity.Status,
                entity.Priority,
                entity.AttemptCount,
                entity.FirstQueuedAt,
                entity.AvailableAt,
                entity.StartedAt,
                entity.LeaseExpiresAt,
                entity.LastError,
                entity.CreatedAt,
                entity.UpdatedAt,
                parent,
                string.IsNullOrWhiteSpace(entity.ParentJobId) ? null : entity.ParentRelationType,
                children,
                audits,
                attempts);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string ComposeJobId(string jobType, string deduplicationKey) => BuildJobId(jobType, deduplicationKey);

    private static AgentRelatedJobReadModel ToRelatedJob(ProcessingJobEntity entity)
        => new(entity.JobId, entity.JobType, entity.DeduplicationKey, entity.ParentRelationType, entity.Status, entity.UpdatedAt);

    public async Task<ResultDayCollectionStatusReadModel?> GetResultDayCollectionStatusAsync(
        string providerType,
        DateOnly targetDate,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var entity = await dbContext.ResultDayCollectionStatuses
                .SingleOrDefaultAsync(
                    x => x.DayKey == ResultDayCollectionStatusKeyFactory.Build(providerType, targetDate),
                    cancellationToken)
                .ConfigureAwait(false);

            return entity is null
                ? null
                : new ResultDayCollectionStatusReadModel(
                    entity.ProviderType,
                    entity.TargetDate,
                    entity.Status,
                    entity.ExpectedRaceCount,
                    entity.CompletedRaceCount,
                    entity.IncompleteReason,
                    entity.LastCompletedAt,
                    entity.RetryAfter,
                    entity.LastError,
                    entity.UpdatedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertResultDayCollectionStatusAsync(
        string providerType,
        DateOnly targetDate,
        ResultDayCollectionState status,
        int? expectedRaceCount,
        int? completedRaceCount,
        string? incompleteReason,
        DateTimeOffset? lastCompletedAt,
        DateTimeOffset? retryAfter,
        string? lastError,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var dayKey = ResultDayCollectionStatusKeyFactory.Build(providerType, targetDate);
            var entity = await dbContext.ResultDayCollectionStatuses
                .SingleOrDefaultAsync(x => x.DayKey == dayKey, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                entity = new ResultDayCollectionStatusEntity
                {
                    DayKey = dayKey,
                    ProviderType = providerType,
                    TargetYear = targetDate.Year,
                    TargetMonth = targetDate.Month,
                    TargetDate = targetDate,
                    CreatedAt = now,
                };
                dbContext.ResultDayCollectionStatuses.Add(entity);
            }

            entity.Status = status;
            entity.ExpectedRaceCount = expectedRaceCount ?? entity.ExpectedRaceCount;
            entity.CompletedRaceCount = completedRaceCount ?? entity.CompletedRaceCount;
            entity.IncompleteReason = incompleteReason;
            entity.LastCompletedAt = lastCompletedAt ?? entity.LastCompletedAt;
            entity.RetryAfter = retryAfter;
            entity.LastError = lastError;
            entity.UpdatedAt = now;

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ResultDayCollectionStatusReadModel>> GetResultDayCollectionStatusesByMonthAsync(
        string providerType,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            return await dbContext.ResultDayCollectionStatuses
                .Where(x => x.ProviderType == providerType && x.TargetYear == year && x.TargetMonth == month)
                .OrderBy(x => x.TargetDate)
                .Select(x => new ResultDayCollectionStatusReadModel(
                    x.ProviderType,
                    x.TargetDate,
                    x.Status,
                    x.ExpectedRaceCount,
                    x.CompletedRaceCount,
                    x.IncompleteReason,
                    x.LastCompletedAt,
                    x.RetryAfter,
                    x.LastError,
                    x.UpdatedAt))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ResultDayCollectionStatusReadModel>> GetResultDayCollectionStatusesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            return await dbContext.ResultDayCollectionStatuses
                .Where(x => x.TargetDate >= from && x.TargetDate <= to)
                .OrderBy(x => x.TargetDate)
                .Select(x => new ResultDayCollectionStatusReadModel(
                    x.ProviderType,
                    x.TargetDate,
                    x.Status,
                    x.ExpectedRaceCount,
                    x.CompletedRaceCount,
                    x.IncompleteReason,
                    x.LastCompletedAt,
                    x.RetryAfter,
                    x.LastError,
                    x.UpdatedAt))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private ProcessingStateDbContext CreateDbContext()
    {
        return new ProcessingStateDbContext(_dbContextOptions);
    }

    private void InitializeDatabase()
    {
        using var dbContext = CreateDbContext();
        dbContext.Database.EnsureCreated();
        dbContext.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS job_attempts (attempt_id TEXT NOT NULL PRIMARY KEY, job_id TEXT NOT NULL, attempt_number INTEGER NOT NULL, status TEXT NOT NULL, error TEXT NULL, started_at TEXT NOT NULL, completed_at TEXT NULL);");
        dbContext.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS ix_job_attempts_job_id_attempt_number ON job_attempts(job_id, attempt_number);");
        var leaseTokenColumnExists = dbContext.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('jobs') WHERE name = 'lease_token'")
            .Any();
        if (!leaseTokenColumnExists)
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE jobs ADD COLUMN lease_token TEXT NULL;");
        var parentJobIdColumnExists = dbContext.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('jobs') WHERE name = 'parent_job_id'")
            .Any();
        if (!parentJobIdColumnExists)
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE jobs ADD COLUMN parent_job_id TEXT NULL;");
        var parentRelationTypeColumnExists = dbContext.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('jobs') WHERE name = 'parent_relation_type'")
            .Any();
        if (!parentRelationTypeColumnExists)
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE jobs ADD COLUMN parent_relation_type TEXT NOT NULL DEFAULT 'AggregatedBy';");
        var dispatchGenerationColumnExists = dbContext.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('jobs') WHERE name = 'dispatch_generation'")
            .Any();
        if (!dispatchGenerationColumnExists)
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE jobs ADD COLUMN dispatch_generation INTEGER NOT NULL DEFAULT 0;");
        dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS ix_jobs_parent_job_id ON jobs(parent_job_id);");
        dbContext.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS collection_dispatch_outbox (
                outbox_id TEXT NOT NULL PRIMARY KEY,
                task_id TEXT NOT NULL,
                job_type TEXT NOT NULL,
                deduplication_key TEXT NOT NULL,
                available_at TEXT NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                dispatched_at TEXT NULL,
                last_error TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS ix_collection_dispatch_outbox_pending ON collection_dispatch_outbox(dispatched_at, available_at);");
        var outboxGenerationColumnExists = dbContext.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('collection_dispatch_outbox') WHERE name = 'dispatch_generation'")
            .Any();
        if (!outboxGenerationColumnExists)
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE collection_dispatch_outbox ADD COLUMN dispatch_generation INTEGER NOT NULL DEFAULT 0;");
        dbContext.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS agent_acquisition_statuses (
                acquisition_key TEXT NOT NULL PRIMARY KEY,
                subject_type TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                provider_type TEXT NULL,
                subject_id TEXT NULL,
                subject_name TEXT NOT NULL,
                related_race_id TEXT NULL,
                origin_job_id TEXT NULL,
                source_url TEXT NULL,
                status TEXT NOT NULL,
                error_code TEXT NULL,
                error_reason TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS ix_agent_acquisition_statuses_updated_at ON agent_acquisition_statuses(updated_at);");
        dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS ix_agent_acquisition_statuses_subject_status_updated_at ON agent_acquisition_statuses(subject_type, status, updated_at);");
        var acquisitionOriginJobIdColumnExists = dbContext.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('agent_acquisition_statuses') WHERE name = 'origin_job_id'")
            .Any();
        if (!acquisitionOriginJobIdColumnExists)
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE agent_acquisition_statuses ADD COLUMN origin_job_id TEXT NULL;");
        dbContext.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS agent_acquisition_history (
                sequence INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                acquisition_key TEXT NOT NULL,
                provider_type TEXT NULL,
                status TEXT NOT NULL,
                error_code TEXT NULL,
                error_reason TEXT NULL,
                origin_job_id TEXT NULL,
                source_url TEXT NULL,
                occurred_at TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS ix_agent_acquisition_history_key_occurred_at ON agent_acquisition_history(acquisition_key, occurred_at);");
        dbContext.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS job_failure_notification_outbox (
                notification_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT NOT NULL,
                job_type TEXT NOT NULL,
                deduplication_key TEXT NOT NULL,
                dispatch_generation INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL,
                error TEXT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                failed_at TEXT NOT NULL,
                available_at TEXT NOT NULL,
                publish_attempt_count INTEGER NOT NULL DEFAULT 0,
                published_at TEXT NULL,
                last_publish_error TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS ix_job_failure_notification_outbox_pending ON job_failure_notification_outbox(published_at, available_at);");
        dbContext.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS job_operation_audits (
                audit_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT NOT NULL,
                operation TEXT NOT NULL,
                previous_status TEXT NOT NULL,
                new_status TEXT NOT NULL,
                actor_id TEXT NOT NULL,
                reason TEXT NULL,
                created_at TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS ix_job_operation_audits_job_created ON job_operation_audits(job_id, created_at);");
        dbContext.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS race_data_collection_statuses (
                race_key TEXT NOT NULL PRIMARY KEY,
                race_date TEXT NOT NULL,
                racecourse TEXT NOT NULL,
                race_number INTEGER NOT NULL,
                race_id TEXT NULL,
                race_name TEXT NULL,
                race_card_url TEXT NULL,
                race_card_status TEXT NOT NULL DEFAULT 'Unknown',
                race_card_error_code TEXT NULL,
                race_card_error_reason TEXT NULL,
                race_card_updated_at TEXT NULL,
                race_result_url TEXT NULL,
                race_result_status TEXT NOT NULL DEFAULT 'Unknown',
                race_result_origin TEXT NULL,
                requested_by_race_id TEXT NULL,
                race_result_error_code TEXT NULL,
                race_result_error_reason TEXT NULL,
                race_result_updated_at TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS ix_race_data_collection_statuses_race_date ON race_data_collection_statuses(race_date);");
        dbContext.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS ix_race_data_collection_statuses_date_course_number ON race_data_collection_statuses(race_date, racecourse, race_number);");
        dbContext.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS agent_acquisition_statuses (
                acquisition_key TEXT NOT NULL PRIMARY KEY,
                subject_type TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                provider_type TEXT NULL,
                subject_id TEXT NULL,
                subject_name TEXT NOT NULL,
                related_race_id TEXT NULL,
                source_url TEXT NULL,
                status TEXT NOT NULL,
                error_code TEXT NULL,
                error_reason TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS ix_agent_acquisition_statuses_updated_at ON agent_acquisition_statuses(updated_at);");
        dbContext.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS ix_agent_acquisition_statuses_subject_status_updated_at ON agent_acquisition_statuses(subject_type, status, updated_at);");
        dbContext.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS result_day_collection_statuses (
                day_key TEXT NOT NULL PRIMARY KEY,
                provider_type TEXT NOT NULL,
                target_year INTEGER NOT NULL,
                target_month INTEGER NOT NULL,
                target_date TEXT NOT NULL,
                status TEXT NOT NULL,
                expected_race_count INTEGER NOT NULL DEFAULT 0,
                completed_race_count INTEGER NOT NULL DEFAULT 0,
                incomplete_reason TEXT NULL,
                last_completed_at TEXT NULL,
                retry_after TEXT NULL,
                last_error TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS ix_result_day_collection_statuses_target_date ON result_day_collection_statuses(target_date);");
        dbContext.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS ix_result_day_collection_statuses_provider_year_month_date ON result_day_collection_statuses(provider_type, target_year, target_month, target_date);");
        dbContext.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS ix_result_day_collection_statuses_provider_status_target_date ON result_day_collection_statuses(provider_type, status, target_date);");
    }

    private async Task UpsertRaceDataCollectionStatusAsync(
        DateOnly raceDate,
        string racecourse,
        int raceNumber,
        string? raceId,
        string? raceName,
        string? sourceUrl,
        RaceDataCollectionState status,
        RaceDataCollectionErrorCode? errorCode,
        string? errorReason,
        DateTimeOffset now,
        bool isRaceCard,
        RaceResultAcquisitionOrigin? raceResultOrigin,
        string? requestedByRaceId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var raceKey = RaceDataCollectionKeyFactory.Build(raceDate, racecourse, raceNumber);
            var entity = await dbContext.RaceDataCollectionStatuses
                .SingleOrDefaultAsync(x => x.RaceKey == raceKey, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                entity = new RaceDataCollectionStatusEntity
                {
                    RaceKey = raceKey,
                    RaceDate = raceDate,
                    Racecourse = racecourse,
                    RaceNumber = raceNumber,
                    CreatedAt = now,
                    UpdatedAt = now,
                    RaceCardStatus = RaceDataCollectionState.Unknown,
                    RaceResultStatus = RaceDataCollectionState.Unknown,
                };
                dbContext.RaceDataCollectionStatuses.Add(entity);
            }

            entity.RaceId ??= raceId;
            entity.RaceName = string.IsNullOrWhiteSpace(raceName) ? entity.RaceName : raceName;
            entity.UpdatedAt = now;

            if (isRaceCard)
            {
                entity.RaceCardUrl = string.IsNullOrWhiteSpace(sourceUrl) ? entity.RaceCardUrl : sourceUrl;
                entity.RaceCardStatus = status;
                entity.RaceCardErrorCode = errorCode;
                entity.RaceCardErrorReason = errorReason;
                entity.RaceCardUpdatedAt = now;
            }
            else
            {
                entity.RaceResultUrl = string.IsNullOrWhiteSpace(sourceUrl) ? entity.RaceResultUrl : sourceUrl;
                entity.RaceResultStatus = status;
                entity.RaceResultOrigin = raceResultOrigin;
                entity.RequestedByRaceId = string.IsNullOrWhiteSpace(requestedByRaceId) ? entity.RequestedByRaceId : requestedByRaceId;
                entity.RaceResultErrorCode = errorCode;
                entity.RaceResultErrorReason = errorReason;
                entity.RaceResultUpdatedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UpdateJobStatusAsync(
        string jobType,
        string deduplicationKey,
        AgentJobStatus status,
        DateTimeOffset? availableAt,
        string? error,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var job = await dbContext.Jobs
                .SingleOrDefaultAsync(
                    x => x.JobType == jobType && x.DeduplicationKey == deduplicationKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (job is null)
            {
                return;
            }

            var previousStatus = job.Status;
            job.Status = status;
            if (availableAt.HasValue)
            {
                job.AvailableAt = availableAt.Value;
            }

            job.LeaseExpiresAt = null;
            job.LeaseToken = null;
            job.LastError = error;
            var now = DateTimeOffset.UtcNow;
            if (status is AgentJobStatus.Succeeded or AgentJobStatus.Failed or AgentJobStatus.DeadLetter)
                await RecordAttemptAsync(dbContext, job, status, error, now, cancellationToken).ConfigureAwait(false);
            job.UpdatedAt = now;
            QueueFailureNotification(dbContext, job, previousStatus, status, error, now);
            await ReconcileParentJobAsync(dbContext, job, now, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReclaimExpiredLeasesAsync(
        ProcessingStateDbContext dbContext,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var runningJobs = await dbContext.Jobs
            .Where(x => x.Status == AgentJobStatus.Running
                && x.LeaseExpiresAt.HasValue)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var expiredJobs = runningJobs
            .Where(x => x.LeaseExpiresAt <= now)
            .ToList();

        if (expiredJobs.Count == 0)
        {
            return;
        }

        foreach (var job in expiredJobs)
        {
            job.Status = AgentJobStatus.Ready;
            job.LeaseExpiresAt = null;
            job.LeaseToken = null;
            job.UpdatedAt = now;
            QueueDispatch(dbContext, job, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogWarning("期限切れのジョブリースを再キューしました。Count={Count}", expiredJobs.Count);
    }

    private static string BuildJobId(string jobType, string deduplicationKey)
        => $"{jobType}:{deduplicationKey}";

    private static async Task RecordAttemptAsync(ProcessingStateDbContext dbContext, ProcessingJobEntity job, AgentJobStatus status, string? error, DateTimeOffset completedAt, CancellationToken cancellationToken)
    {
        var number = (await dbContext.JobAttempts.Where(x => x.JobId == job.JobId).MaxAsync(x => (int?)x.AttemptNumber, cancellationToken).ConfigureAwait(false) ?? 0) + 1;
        job.AttemptCount = number;
        dbContext.JobAttempts.Add(new JobAttemptEntity
        {
            AttemptId = $"{job.JobId}:{number}", JobId = job.JobId, AttemptNumber = number,
            Status = status, Error = error, StartedAt = job.StartedAt ?? completedAt, CompletedAt = completedAt
        });
    }

    public async Task<AgentJobSearchResult> SearchJobStatusesAsync(
        string? view,
        string? query,
        string? targetDate,
        string? jobType,
        AgentJobStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var allItems = await GetJobStatusesAsync(jobType, status, int.MaxValue, cancellationToken).ConfigureAwait(false);
        var daySummaries = allItems
            .Select(x => new { Job = x, Date = ExtractTargetDate(x.DeduplicationKey) })
            .Where(x => x.Date is not null)
            .GroupBy(x => x.Date!, StringComparer.Ordinal)
            .OrderByDescending(x => x.Key)
            .Take(7)
            .Select(x => new AgentJobDaySummary(
                x.Key,
                x.Count(),
                x.Count(item => item.Job.Status == AgentJobStatus.Succeeded),
                x.Count(item => item.Job.Status is AgentJobStatus.Failed or AgentJobStatus.DeadLetter),
                x.Count(item => item.Job.Status is AgentJobStatus.Ready or AgentJobStatus.Pending or AgentJobStatus.Running or AgentJobStatus.WaitingDependency)))
            .ToList();

        IEnumerable<AgentJobStatusReadModel> filtered = view switch
        {
            "action" => allItems.Where(x => x.Status is AgentJobStatus.Failed or AgentJobStatus.DeadLetter),
            "running" => allItems.Where(x => x.Status == AgentJobStatus.Running),
            "ready" => allItems.Where(x => x.Status is AgentJobStatus.Ready or AgentJobStatus.WaitingDependency),
            "recent" => allItems.Where(x => x.Status == AgentJobStatus.Succeeded),
            _ => allItems
        };
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            filtered = filtered.Where(x => x.JobType.Contains(term, StringComparison.OrdinalIgnoreCase)
                || DisplayJobType(x.JobType).Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.DeduplicationKey.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(targetDate))
            filtered = filtered.Where(x => string.Equals(ExtractTargetDate(x.DeduplicationKey), targetDate.Trim(), StringComparison.Ordinal));

        var materialized = filtered.ToList();
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var safePage = Math.Max(1, page);
        return new AgentJobSearchResult(materialized.Skip((safePage - 1) * safePageSize).Take(safePageSize).ToList(), materialized.Count, daySummaries);
    }

    private static string? ExtractTargetDate(string value)
    {
        var match = System.Text.RegularExpressions.Regex.Match(value, @"\d{4}-\d{2}-\d{2}");
        return match.Success ? match.Value : null;
    }

    private static string DisplayJobType(string value) => value switch
    {
        "ResultMonthDiscoveryRequest" => "月次レース結果の確認",
        "RaceResultCollectionRequest" => "レース結果の収集",
        "RaceCardCollectionRequest" => "出走表の収集",
        _ => value
    };

    public async Task<IReadOnlyList<AgentAcquisitionHistoryReadModel>> GetAgentAcquisitionHistoryAsync(
        string acquisitionKey,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var entries = await dbContext.AgentAcquisitionHistory
                .Where(x => x.AcquisitionKey == acquisitionKey)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return entries
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.Sequence)
                .Select(x => new AgentAcquisitionHistoryReadModel(
                    x.Sequence, x.AcquisitionKey, x.ProviderType, x.Status, x.ErrorCode,
                    x.ErrorReason, x.OriginJobId, x.SourceUrl, x.OccurredAt))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> UpdateLeasedCollectionTaskAsync(
        string jobType,
        string deduplicationKey,
        string leaseToken,
        AgentJobStatus status,
        DateTimeOffset? availableAt,
        string? error,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var job = await dbContext.Jobs.SingleOrDefaultAsync(
                x => x.JobType == jobType
                    && x.DeduplicationKey == deduplicationKey
                    && x.Status == AgentJobStatus.Running
                    && x.LeaseToken == leaseToken,
                cancellationToken).ConfigureAwait(false);
            if (job is null) return false;

            var now = DateTimeOffset.UtcNow;
            var previousStatus = job.Status;
            job.Status = status;
            job.LeaseToken = null;
            job.LeaseExpiresAt = null;
            job.LastError = error;
            job.UpdatedAt = now;
            if (availableAt.HasValue)
            {
                job.AvailableAt = availableAt.Value;
                QueueDispatch(dbContext, job, availableAt.Value);
            }
            if (status is AgentJobStatus.Succeeded or AgentJobStatus.Failed or AgentJobStatus.DeadLetter)
            {
                await RecordAttemptAsync(dbContext, job, status, error, now, cancellationToken).ConfigureAwait(false);
            }
            QueueFailureNotification(dbContext, job, previousStatus, status, error, now);

            await ReconcileParentJobAsync(dbContext, job, now, cancellationToken).ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    private static async Task ReconcileParentJobAsync(
        ProcessingStateDbContext dbContext,
        ProcessingJobEntity child,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (child.ParentRelationType != JobRelationType.AggregatedBy
            || string.IsNullOrWhiteSpace(child.ParentJobId)
            || child.Status is not (AgentJobStatus.Succeeded or AgentJobStatus.Failed or AgentJobStatus.DeadLetter))
            return;

        var parent = await dbContext.Jobs.SingleOrDefaultAsync(x => x.JobId == child.ParentJobId, cancellationToken)
            .ConfigureAwait(false);
        if (parent is null || parent.Status != AgentJobStatus.WaitingDependency)
            return;

        var children = await dbContext.Jobs.Where(x => x.ParentJobId == parent.JobId).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (children.Count == 0)
            return;

        var completedCount = children.Count(x => x.Status == AgentJobStatus.Succeeded);
        var terminalCount = children.Count(x => x.Status is AgentJobStatus.Succeeded or AgentJobStatus.Failed or AgentJobStatus.DeadLetter);
        if (terminalCount < children.Count)
            return;

        var failedChildren = children.Where(x => x.Status is AgentJobStatus.Failed or AgentJobStatus.DeadLetter).ToList();
        var error = failedChildren.Count == 0
            ? null
            : string.Join(Environment.NewLine, failedChildren.Select(x => $"{x.DeduplicationKey}: {x.LastError ?? x.Status.ToString()}"));
        parent.Status = failedChildren.Count == 0 ? AgentJobStatus.Succeeded : AgentJobStatus.Failed;
        parent.LastError = error;
        parent.LeaseExpiresAt = null;
        parent.LeaseToken = null;
        parent.UpdatedAt = now;

        if (parent.JobType != AgentJobType.ResultDayCollectionRequest)
            return;

        const string marker = ":result-day-collection:";
        var markerIndex = parent.DeduplicationKey.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex <= 0
            || !DateOnly.TryParseExact(parent.DeduplicationKey[(markerIndex + marker.Length)..], "yyyy-MM-dd", out var raceDate))
            return;
        var providerType = parent.DeduplicationKey[..markerIndex];
        var dayKey = ResultDayCollectionStatusKeyFactory.Build(providerType, raceDate);
        var day = await dbContext.ResultDayCollectionStatuses.SingleOrDefaultAsync(x => x.DayKey == dayKey, cancellationToken)
            .ConfigureAwait(false);
        if (day is null)
            return;

        day.Status = failedChildren.Count == 0 ? ResultDayCollectionState.Complete : ResultDayCollectionState.Incomplete;
        day.ExpectedRaceCount = children.Count;
        day.CompletedRaceCount = completedCount;
        day.IncompleteReason = error;
        day.LastError = error;
        day.LastCompletedAt = failedChildren.Count == 0 ? now : null;
        day.RetryAfter = null;
        day.UpdatedAt = now;
    }

    private static void QueueDispatch(ProcessingStateDbContext dbContext, ProcessingJobEntity job, DateTimeOffset availableAt)
    {
        if (!CollectionDispatchPolicy.IsDispatchable(job.JobType)) return;
        job.DispatchGeneration += 1;
        dbContext.DispatchOutbox.Add(new CollectionDispatchOutboxEntity
        {
            OutboxId = Guid.NewGuid().ToString("N"),
            TaskId = job.JobId,
            JobType = job.JobType,
            DeduplicationKey = job.DeduplicationKey,
            DispatchGeneration = job.DispatchGeneration,
            AvailableAt = availableAt,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    public async Task<ForceRequeueJobResult> ForceRequeueJobAsync(
        string jobId,
        DateTimeOffset expectedUpdatedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var job = await dbContext.Jobs.SingleOrDefaultAsync(x => x.JobId == jobId, cancellationToken).ConfigureAwait(false);
            if (job is null) return ForceRequeueJobResult.NotFound;
            if (job.UpdatedAt != expectedUpdatedAt) return ForceRequeueJobResult.Conflict;

            var previousStatus = job.Status;
            job.Status = AgentJobStatus.Ready;
            job.AvailableAt = now;
            job.StartedAt = null;
            job.LeaseExpiresAt = null;
            job.LeaseToken = null;
            job.LastError = null;
            job.UpdatedAt = now;
            dbContext.JobOperationAudits.Add(new JobOperationAuditEntity
            {
                AuditId = Guid.NewGuid().ToString("N"),
                JobId = job.JobId,
                Operation = "ManualRequeue",
                PreviousStatus = previousStatus,
                NewStatus = AgentJobStatus.Ready,
                ActorId = "admin-ui",
                Reason = "管理画面から再キュー",
                CreatedAt = now
            });
            QueueDispatch(dbContext, job, now);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ForceRequeueJobResult.Requeued;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ForceRequeueJobResult> RerunJobAsync(string jobId, DateTimeOffset expectedUpdatedAt, string actorId, string? reason, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var job = await db.Jobs.SingleOrDefaultAsync(x => x.JobId == jobId, cancellationToken).ConfigureAwait(false);
            if (job is null) return ForceRequeueJobResult.NotFound;
            if (job.UpdatedAt != expectedUpdatedAt) return ForceRequeueJobResult.Conflict;
            if (job.Status is not (AgentJobStatus.Failed or AgentJobStatus.DeadLetter))
                return ForceRequeueJobResult.Conflict;
            if (!string.IsNullOrWhiteSpace(job.ParentJobId) && job.ParentRelationType == JobRelationType.AggregatedBy)
                return ForceRequeueJobResult.Conflict;

            var previousStatus = job.Status;
            var aggregatedChildren = await db.Jobs
                .Where(x => x.ParentJobId == jobId && x.ParentRelationType == JobRelationType.AggregatedBy)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            job.Status = AgentJobStatus.Ready;
            job.AvailableAt = now;
            job.StartedAt = null;
            job.LeaseToken = null;
            job.LeaseExpiresAt = null;
            job.LastError = null;
            job.UpdatedAt = now;

            var failed = aggregatedChildren
                .Where(x => x.Status is AgentJobStatus.Failed or AgentJobStatus.DeadLetter)
                .ToList();
            foreach (var child in failed)
            {
                child.Status = AgentJobStatus.Ready;
                child.AvailableAt = now;
                child.StartedAt = null;
                child.LeaseToken = null;
                child.LeaseExpiresAt = null;
                child.LastError = null;
                child.UpdatedAt = now;
                QueueDispatch(db, child, now);
            }
            db.JobOperationAudits.Add(new JobOperationAuditEntity
            {
                AuditId = Guid.NewGuid().ToString("N"),
                JobId = job.JobId,
                Operation = aggregatedChildren.Count == 0 ? "RerunJob" : "RerunAggregate",
                PreviousStatus = previousStatus,
                NewStatus = AgentJobStatus.Ready,
                ActorId = string.IsNullOrWhiteSpace(actorId) ? "Admin UI" : actorId,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                CreatedAt = now
            });
            QueueDispatch(db, job, now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ForceRequeueJobResult.Requeued;
        }
        finally { _gate.Release(); }
    }

    public async Task<(ForceRequeueJobResult Result, string? JobId)> ReacquireCompletedJobAsync(
        string sourceJobId, DateTimeOffset expectedUpdatedAt, string actorId, string? reason,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var source = await db.Jobs.SingleOrDefaultAsync(x => x.JobId == sourceJobId, cancellationToken).ConfigureAwait(false);
            if (source is null) return (ForceRequeueJobResult.NotFound, null);
            if (source.UpdatedAt != expectedUpdatedAt || source.Status != AgentJobStatus.Succeeded)
                return (ForceRequeueJobResult.Conflict, null);

            var deduplicationKey = $"{source.DeduplicationKey}:reacquire:{now:yyyyMMddHHmmssfff}";
            var newJobId = BuildJobId(source.JobType, deduplicationKey);
            var job = new ProcessingJobEntity
            {
                JobId = newJobId, JobType = source.JobType, DeduplicationKey = deduplicationKey,
                Payload = source.Payload, Status = AgentJobStatus.Ready, Priority = source.Priority,
                FirstQueuedAt = now, AvailableAt = now, CreatedAt = now, UpdatedAt = now
            };
            db.Jobs.Add(job);
            db.JobOperationAudits.Add(new JobOperationAuditEntity
            {
                AuditId = Guid.NewGuid().ToString("N"), JobId = source.JobId, Operation = "Reacquire",
                PreviousStatus = source.Status, NewStatus = source.Status,
                ActorId = string.IsNullOrWhiteSpace(actorId) ? "Admin UI" : actorId,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), CreatedAt = now
            });
            QueueDispatch(db, job, now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (ForceRequeueJobResult.Requeued, newJobId);
        }
        finally { _gate.Release(); }
    }

    public async Task<ForceRequeueJobResult> CancelJobAsync(
        string jobId,
        DateTimeOffset expectedUpdatedAt,
        string actorId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("取消理由は必須です。", nameof(reason));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = CreateDbContext();
            var job = await dbContext.Jobs.SingleOrDefaultAsync(x => x.JobId == jobId, cancellationToken).ConfigureAwait(false);
            if (job is null) return ForceRequeueJobResult.NotFound;
            if (job.UpdatedAt != expectedUpdatedAt) return ForceRequeueJobResult.Conflict;

            var previousStatus = job.Status;
            job.Status = AgentJobStatus.Cancelled;
            job.DispatchGeneration += 1;
            job.LeaseToken = null;
            job.LeaseExpiresAt = null;
            job.StartedAt = null;
            job.UpdatedAt = now;

            var pendingDispatches = await dbContext.DispatchOutbox
                .Where(x => x.TaskId == job.JobId && x.DispatchedAt == null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var dispatch in pendingDispatches)
            {
                dispatch.DispatchedAt = now;
                dispatch.LastError = "Cancelled before dispatch.";
                dispatch.UpdatedAt = now;
            }

            dbContext.JobOperationAudits.Add(new JobOperationAuditEntity
            {
                AuditId = Guid.NewGuid().ToString("N"),
                JobId = job.JobId,
                Operation = "ManualCancel",
                PreviousStatus = previousStatus,
                NewStatus = AgentJobStatus.Cancelled,
                ActorId = string.IsNullOrWhiteSpace(actorId) ? "admin-ui" : actorId,
                Reason = reason.Trim(),
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ForceRequeueJobResult.Requeued;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void QueueFailureNotification(
        ProcessingStateDbContext dbContext,
        ProcessingJobEntity job,
        AgentJobStatus previousStatus,
        AgentJobStatus status,
        string? error,
        DateTimeOffset now)
    {
        if (previousStatus == status || status is not (AgentJobStatus.Failed or AgentJobStatus.DeadLetter)) return;

        dbContext.JobFailureNotifications.Add(new JobFailureNotificationEntity
        {
            NotificationId = Guid.NewGuid().ToString("N"),
            JobId = job.JobId,
            JobType = job.JobType,
            DeduplicationKey = job.DeduplicationKey,
            Status = status.ToString(),
            Error = error,
            AttemptCount = job.AttemptCount,
            FailedAt = now,
            AvailableAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

}
