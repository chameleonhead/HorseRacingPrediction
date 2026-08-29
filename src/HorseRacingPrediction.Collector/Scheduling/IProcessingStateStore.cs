namespace HorseRacingPrediction.Collector.Scheduling;

public interface IProcessingStateStore
{
    Task EnqueuePredictionCandidatesAsync(IEnumerable<string> raceIds, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> TakeReadyPredictionCandidatesAsync(DateTimeOffset now, TimeSpan minAge, int maxCount, CancellationToken cancellationToken = default);
    Task MarkPredictionCompletedAsync(string raceId, CancellationToken cancellationToken = default);
    Task RequeuePredictionCandidateAsync(string raceId, DateTimeOffset now, string error, CancellationToken cancellationToken = default);
    Task<bool> HasMarkerAsync(string markerType, string markerKey, CancellationToken cancellationToken = default);
    Task MarkMarkerAsync(string markerType, string markerKey, CancellationToken cancellationToken = default);
    Task EnqueueJobAsync(string jobType, string deduplicationKey, string payload, DateTimeOffset now, int priority = 0, CancellationToken cancellationToken = default);
    Task ScheduleJobAsync(string jobType, string deduplicationKey, string payload, DateTimeOffset now, int priority = 0, string? parentJobId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AcquiredProcessingJob>> AcquireReadyJobsAsync(string jobType, DateTimeOffset now, TimeSpan minAge, int maxCount, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task<LeasedCollectionTask?> AcquireCollectionTaskAsync(string jobType, string deduplicationKey, long dispatchGeneration, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task<LeasedCollectionTask?> AcquireCollectionTaskAsync(string jobType, string deduplicationKey, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task<bool> CompleteCollectionTaskAsync(string jobType, string deduplicationKey, string leaseToken, CancellationToken cancellationToken = default);
    Task<bool> FailCollectionTaskAsync(string jobType, string deduplicationKey, string leaseToken, string? error, CancellationToken cancellationToken = default);
    Task<bool> RequeueCollectionTaskAsync(string jobType, string deduplicationKey, string leaseToken, DateTimeOffset availableAt, string? error, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PendingCollectionTaskDispatch>> GetPendingCollectionTaskDispatchesAsync(DateTimeOffset now, int maxCount, CancellationToken cancellationToken = default);
    Task MarkCollectionTaskDispatchedAsync(string outboxId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task MarkCollectionTaskDispatchFailedAsync(string outboxId, DateTimeOffset now, string error, CancellationToken cancellationToken = default);
    Task CompleteJobAsync(string jobType, string deduplicationKey, CancellationToken cancellationToken = default);
    Task WaitForDependenciesAsync(string jobType, string deduplicationKey, CancellationToken cancellationToken = default);
    Task FailJobAsync(string jobType, string deduplicationKey, string? error, CancellationToken cancellationToken = default);
    Task RequeueJobAsync(string jobType, string deduplicationKey, DateTimeOffset now, string? error, CancellationToken cancellationToken = default, DateTimeOffset? availableAt = null);
    Task<bool> ForceRequeueJobAsync(string jobType, string deduplicationKey, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<ForceRequeueJobResult> ForceRequeueJobAsync(string jobId, DateTimeOffset expectedUpdatedAt, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<ForceRequeueJobResult> CancelJobAsync(string jobId, DateTimeOffset expectedUpdatedAt, string actorId, string reason, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<int> RequeueRunningJobsAsync(IEnumerable<string> jobTypes, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetActiveJobPayloadsAsync(string jobType, CancellationToken cancellationToken = default);
    Task<int> GetAttemptCountAsync(string jobType, string deduplicationKey, CancellationToken cancellationToken = default);
    Task MarkJobAsDeadLetterAsync(string jobType, string deduplicationKey, string? error, CancellationToken cancellationToken = default);
    Task UpsertRaceCardCollectionStatusAsync(DateOnly raceDate, string racecourse, int raceNumber, string? raceId, string? raceName, string? sourceUrl, RaceDataCollectionState status, RaceDataCollectionErrorCode? errorCode, string? errorReason, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task UpsertRaceResultCollectionStatusAsync(DateOnly raceDate, string racecourse, int raceNumber, string? raceId, string? raceName, string? sourceUrl, RaceDataCollectionState status, RaceResultAcquisitionOrigin origin, string? requestedByRaceId, RaceDataCollectionErrorCode? errorCode, string? errorReason, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RaceDataCollectionStatusReadModel>> GetRaceDataCollectionStatusesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task UpsertAgentAcquisitionStatusAsync(string acquisitionKey, AgentAcquisitionSubjectType subjectType, AgentAcquisitionOperationType operationType, string? providerType, string? subjectId, string subjectName, string? relatedRaceId, string? sourceUrl, RaceDataCollectionState status, RaceDataCollectionErrorCode? errorCode, string? errorReason, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentAcquisitionStatusReadModel>> GetAgentAcquisitionStatusesAsync(DateOnly from, DateOnly to, AgentAcquisitionSubjectType? subjectType, RaceDataCollectionState? status, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentJobStatusReadModel>> GetJobStatusesAsync(string? jobType, AgentJobStatus? status, int limit, CancellationToken cancellationToken = default);
    Task<AgentJobDetailReadModel?> GetJobDetailAsync(string jobId, CancellationToken cancellationToken = default);
    Task<ResultDayCollectionStatusReadModel?> GetResultDayCollectionStatusAsync(string providerType, DateOnly targetDate, CancellationToken cancellationToken = default);
    Task UpsertResultDayCollectionStatusAsync(string providerType, DateOnly targetDate, ResultDayCollectionState status, int? expectedRaceCount, int? completedRaceCount, string? incompleteReason, DateTimeOffset? lastCompletedAt, DateTimeOffset? retryAfter, string? lastError, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResultDayCollectionStatusReadModel>> GetResultDayCollectionStatusesByMonthAsync(string providerType, int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResultDayCollectionStatusReadModel>> GetResultDayCollectionStatusesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
