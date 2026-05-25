using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class ProcessingStateStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DbContextOptions<ProcessingStateDbContext> _dbContextOptions;
    private readonly AgentProcessingOptions _options;
    private readonly ILogger<ProcessingStateStore> _logger;

    public ProcessingStateStore(IOptions<AgentProcessingOptions> options, ILogger<ProcessingStateStore> logger)
    {
        _options = options.Value;

        var dir = _options.StateDirectory;
        var stateDirectory = string.IsNullOrWhiteSpace(dir)
            ? Path.Combine(AppContext.BaseDirectory, "agent-processing-state")
            : dir;

        Directory.CreateDirectory(stateDirectory);

        var jobStoreFileName = string.IsNullOrWhiteSpace(_options.JobStoreFileName)
            ? "processing-jobs.db"
            : _options.JobStoreFileName;
        var dbPath = Path.Combine(stateDirectory, jobStoreFileName);

        _dbContextOptions = new DbContextOptionsBuilder<ProcessingStateDbContext>()
            .UseSqlite($"Data Source={dbPath}")
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

    public async Task<bool> IsTextInsightRecordedAsync(string insightKey, CancellationToken cancellationToken = default)
    {
        return await HasMarkerAsync("TextInsight", insightKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkTextInsightRecordedAsync(string insightKey, CancellationToken cancellationToken = default)
    {
        await MarkMarkerAsync("TextInsight", insightKey, cancellationToken).ConfigureAwait(false);
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

            dbContext.Jobs.Add(new ProcessingJobEntity
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
            });

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
                dbContext.Jobs.Add(new ProcessingJobEntity
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
                });

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            job.Payload = payload;
            job.Priority = priority;
            job.UpdatedAt = now;

            if (job.Status is AgentJobStatus.Succeeded or AgentJobStatus.Failed or AgentJobStatus.Cancelled or AgentJobStatus.DeadLetter)
            {
                job.Status = AgentJobStatus.Ready;
                job.AvailableAt = now;
                job.StartedAt = null;
                job.LeaseExpiresAt = null;
                job.LastError = null;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<(string DeduplicationKey, string Payload)>> AcquireReadyJobsAsync(
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
                job.Status = AgentJobStatus.Running;
                job.StartedAt = now;
                job.LeaseExpiresAt = now.Add(leaseDuration);
                job.UpdatedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return readyJobs
                .Select(x => (x.DeduplicationKey, x.Payload))
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
            job.AttemptCount += 1;
            job.LastError = error;
            job.UpdatedAt = now;
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
            job.LastError = null;
            job.UpdatedAt = now;
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
                job.LastError = null;
                job.AvailableAt = now;
                job.UpdatedAt = now;
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
            entity.SourceUrl = string.IsNullOrWhiteSpace(sourceUrl) ? entity.SourceUrl : sourceUrl;
            entity.Status = status;
            entity.ErrorCode = errorCode;
            entity.ErrorReason = errorReason;
            entity.UpdatedAt = now;

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
                    x.SubjectType,
                    x.OperationType,
                    x.ProviderType,
                    x.SubjectId,
                    x.SubjectName,
                    x.RelatedRaceId,
                    x.SourceUrl,
                    x.Status,
                    x.ErrorCode,
                    x.ErrorReason,
                    x.UpdatedAt))
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
                entity.UpdatedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string ComposeJobId(string jobType, string deduplicationKey) => BuildJobId(jobType, deduplicationKey);

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

            job.Status = status;
            if (availableAt.HasValue)
            {
                job.AvailableAt = availableAt.Value;
            }

            job.LeaseExpiresAt = null;
            job.LastError = error;
            job.UpdatedAt = DateTimeOffset.UtcNow;
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
            job.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogWarning("期限切れのジョブリースを再キューしました。Count={Count}", expiredJobs.Count);
    }

    private static string BuildJobId(string jobType, string deduplicationKey)
        => $"{jobType}:{deduplicationKey}";

}
