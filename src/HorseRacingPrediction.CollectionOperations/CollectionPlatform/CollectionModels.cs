
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;


public readonly record struct ResourceKey(CollectionResourceType Type, string Provider, string Id)
{
    public ResourceKey Normalize() => new(Type, Provider.Trim().ToUpperInvariant(), Id.Trim());
}

public readonly record struct CollectionDefinitionId(string Value)
{
    public override string ToString() => Value;
}

public sealed record CollectionSchedule(bool ShouldCollect, DateTimeOffset? NextCollectionAt,
    CollectionPriority Priority, CollectionLane Lane, string Reason);

public interface ICollectionSchedulePolicy
{
    CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now);
}

public sealed record CollectionStateSnapshot(ResourceKey Resource, CollectionDefinitionId Definition,
    int AppliedRevision, int RequiredRevision, DateTimeOffset? LastCollectedAt,
    DateTimeOffset? NextCollectionAt, CollectionStateStatus Status,
    IReadOnlyList<RaceArtifactSnapshot>? RaceArtifacts = null);

public sealed record CollectionRequestReceipt(Guid RequestId, Guid? TaskId, bool CreatedTask, bool DeferredByRepairHold = false);
public sealed record CollectionRequestBatchItem(string ItemKey, ResourceKey Resource,
    CollectionDefinitionId Definition, int RequestedRevision, CollectionReason Reason,
    CollectionLane Lane, int Priority, Uri? ExplicitUrl, DateOnly? EffectiveDate,
    IReadOnlyDictionary<string, string>? Attributes);
public sealed record CollectionRequestBatchOutcome(string ItemKey, string Status,
    CollectionRequestReceipt? Receipt = null, string? ErrorCode = null, string? Message = null);
public sealed record CollectionResourceSuppressionResult(int CancelledTasks, int RunningCancellationRequests);
public sealed record ObsoleteSubjectProfileTask(Guid TaskId, ResourceKey Resource,
    CollectionDefinitionId Definition, CollectionTaskStatus Status, int AttemptCount,
    string ErrorCode, string? RequestedByRaceId,
    string? Name = null, string? SourceIdentity = null, Uri? SourceUrl = null,
    int RequestedRevision = 0, CollectionLane Lane = CollectionLane.Normal,
    int Priority = (int)CollectionPriority.Normal, DateOnly? EffectiveDate = null,
    DateTimeOffset? AvailableAt = null, DateTimeOffset? CreatedAt = null,
    DateTimeOffset? LatestFailedAt = null,
    CollectionAttemptResult? LatestResult = null);
public sealed record ObsoleteSubjectProfileTaskCleanupResult(bool Executed, int SelectedCount,
    int CancelledCount, int RunningCancellationRequests,
    IReadOnlyList<ObsoleteSubjectProfileTask> Tasks);
public sealed record LegacyRaceDetailMergeReport(bool DryRun, int SourceResources, int TargetResources,
    int Requests, int Tasks, int Attempts, int Locations, int States, int SupplementRequests,
    IReadOnlyList<string> Errors);
public sealed record CollectionResourceSuppressionPreview(int PendingTasks, int RunningTasks)
{
    public int TotalTasks => PendingTasks + RunningTasks;
}

public sealed class CollectionResourceSuppressedException(ResourceKey resource, string reason)
    : InvalidOperationException($"Collection resource {resource.Type}/{resource.Provider}/{resource.Id} is suppressed: {reason}")
{
    public ResourceKey Resource { get; } = resource;
    public string SuppressionReason { get; } = reason;
}

public sealed class CollectionRequestIdempotencyMismatchException(string batchId)
    : InvalidOperationException($"Collection request idempotency mismatch for {batchId}.");

public sealed record CollectionTaskAcquireResult(CollectionTaskAcquireStatus Status, LeasedCollectionTask? Task = null);

public sealed record CollectionWakeSignal(Guid WakeId, Guid DispatchEnvelopeId, string ReservationToken,
    int ContractVersion = 1);
public enum CollectionDispatchCycleOutcome
{
    NoCandidates,
    Reserved,
    CapacityFull,
    CandidateRejected,
    ReserveConflict,
    WakeSent,
    WakeSendDefiniteFailure,
    WakeSendAmbiguousFailure,
    WakeReceiptPersistFailure,
}

public interface ICollectionDispatchTelemetry
{
    Task RecordDispatchCycleAsync(CollectionDispatchCycleOutcome outcome, CollectionLane? lane = null,
        string? definitionId = null, CancellationToken cancellationToken = default);
    Task RecordAcquireAsync(CollectionExecutionAcquireStatus status, CollectionExecutionNoWorkReason? reason,
        CollectionLane? lane = null, string? definitionId = null, CancellationToken cancellationToken = default);
    Task RecordReservationReleaseAsync(CollectionReservationReleaseOutcome outcome,
        CancellationToken cancellationToken = default);
    Task RecordLeaseReclaimedAsync(CancellationToken cancellationToken = default);
    Task RecordTerminalCompletionAsync(CollectionLane lane, string definitionId, CollectionTaskStatus status,
        CancellationToken cancellationToken = default);
    Task RecordTerminalCompletionLookupAsync(Func<CancellationToken, Task<CollectionDispatchTaskTelemetryState?>> lookup,
        CancellationToken cancellationToken = default);
    Task QueueSnapshotAsync(Func<CancellationToken, Task<CollectionDispatchTelemetrySnapshot>> query,
        CancellationToken cancellationToken = default);
}

public sealed record CollectionDispatchTelemetrySnapshot(
    int ReadyMissingCurrentOutbox,
    int CardinalityAnomalyTasks,
    int ActiveEligibleReservations,
    int ExpiredEligibleReservations,
    int InFlightExecutionLeases,
    int MaxInFlightEnvelopes,
    int EligibleInFlightCount,
    IReadOnlyList<CollectionDispatchLaneSnapshot> Lanes);

public sealed record CollectionDispatchLaneSnapshot(
    CollectionLane Lane,
    string DefinitionId,
    int EligibleReadyRows,
    double OldestEligibleAgeSeconds,
    int ActiveEligibleReservations,
    int ExpiredEligibleReservations);

public sealed record CollectionDispatchTaskTelemetryState(
    CollectionLane Lane, string DefinitionId, CollectionTaskStatus Status);

public sealed record CollectionExecutionAcquireResult(CollectionExecutionAcquireStatus Status,
    Guid? ExecutionBatchId = null, string? LeaseToken = null,
    CollectionDispatchEnvelope? Envelope = null, DateTimeOffset? StartBefore = null,
    CollectionExecutionNoWorkReason? NoWorkReason = null,
    CollectionReservationReleaseOutcome? ReservationReleaseOutcome = null)
{
    public bool SafeToReleaseReservation => Status == CollectionExecutionAcquireStatus.NoWork
        && NoWorkReason is (CollectionExecutionNoWorkReason.PipelinePaused
            or CollectionExecutionNoWorkReason.TaskIneligible
            or CollectionExecutionNoWorkReason.RepairHold
            or CollectionExecutionNoWorkReason.ResourceUnavailable);
}
public sealed record CollectionExecutionAcquireRequest(CollectionWakeSignal Wake, string QueueMessageId);
public sealed record CollectionExecutionStartRequest(string LeaseToken, int LeaseSeconds,
    string? LambdaRequestId = null);
public sealed record CollectionExecutionCompleteRequest(string LeaseToken);

public sealed record LeasedCollectionTask(Guid TaskId, Guid RequestId, ResourceKey Resource,
    CollectionDefinitionId Definition, int RequestedRevision, CollectionReason Reason,
    CollectionLane Lane, int Priority, string LeaseToken, DateTimeOffset LeaseExpiresAt,
    DateOnly? EffectiveDate, IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<ResourceLocationCandidate>? Locations = null,
    long RaceHoldGeneration = 0, string? EntryAssignmentFingerprint = null);

public sealed record CollectionStageOutcome(string Stage, RaceArtifactKind Artifact,
    CollectionAttemptResult Result, string? ErrorCode = null, string? ErrorMessage = null,
    Uri? RequestedUrl = null, Uri? FinalUrl = null, bool Persisted = false);

public sealed record SubjectIdentificationCandidate(string Name, string Url, string? Evidence = null);

public sealed record RaceArtifactSnapshot(RaceArtifactKind Artifact, RaceArtifactStatus Status,
    int AppliedRevision, int RequiredRevision, DateTimeOffset? LastObservedAt,
    DateTimeOffset? LastPersistedAt, DateTimeOffset? NextDueAt, string? ErrorCode,
    string? ErrorMessage);

public sealed record RaceSchedulingEvidence(DateTimeOffset? OfficialStartAt,
    string? Provenance, DateTimeOffset? VerifiedAt);

public sealed record CollectionAttemptCompletion(CollectionAttemptResult Result, string? ErrorCode = null,
    string? ErrorMessage = null, Uri? RequestedUrl = null, Uri? FinalUrl = null,
    int? HttpStatusCode = null, string? PageIdentification = null,
    DateTimeOffset? RetryAt = null, DateTimeOffset? NextCollectionAt = null,
    IReadOnlyList<ResourceLocationOutcome>? LocationOutcomes = null,
    CollectionFailureImpact FailureImpact = CollectionFailureImpact.StopPipeline,
    IReadOnlyList<CollectionStageOutcome>? StageOutcomes = null,
    RaceSchedulingEvidence? RaceEvidence = null,
    IReadOnlyList<SubjectIdentificationCandidate>? IdentificationCandidates = null);

public sealed record RevisionImpact(RevisionImpactScopeType ScopeType, string ScopePayload);

public sealed record RevisionResourceCandidate(ResourceKey Resource, DateOnly? EffectiveDate,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record RevisionImpactPreview(CollectionDefinitionId Definition, int Revision,
    RevisionImpact Impact, int TotalCandidates, IReadOnlyList<ResourceKey> AffectedResources);

public sealed record RevisionRecollectionExpansion(CollectionDefinitionId Definition, int Revision,
    string BatchId, int Affected, int RequestsCreated, int ExistingRequests);

public sealed record RevisionRecollectionProgress(CollectionDefinitionId Definition, int Revision,
    int Affected, int Completed, int Pending, int Failed);

public sealed record CollectionBulkTarget(ResourceKey Resource, DateOnly? EffectiveDate = null,
    IReadOnlyDictionary<string, string>? Attributes = null);
public sealed record CollectionBatchResourceStatus(ResourceKey Resource, int RequestedRevision,
    CollectionTaskStatus? LatestTaskStatus, CollectionStateStatus? StateStatus,
    int AppliedRevision, int RequiredRevision);
public sealed record CollectionBulkPreview(CollectionDefinitionId Definition, int Revision,
    int TargetCount, IReadOnlyList<ResourceKey> Resources);
public sealed record CollectionBulkExecution(string BatchId, int TargetCount, int TasksCreated,
    IReadOnlyList<CollectionRequestReceipt> Requests);

public interface INamedRevisionImpactCondition
{
    string Name { get; }
    bool Matches(RevisionResourceCandidate candidate);
}

public sealed record ResourceLocationCandidate(long LocationId, Uri Url, ResourceLocationSource Source,
    ResourceLocationStatus Status, DateTimeOffset? LastVerifiedAt, RaceArtifactKind? Artifact = null);

public sealed record ResourceLocationOutcome(long LocationId, CollectionAttemptResult Result,
    string? ErrorCode = null, RaceArtifactKind? Artifact = null);

public sealed record CollectionTaskNotification(Guid TaskId, long DispatchGeneration, int ContractVersion = 1)
{
    public const int CurrentContractVersion = 1;

    public bool IsSupported()
        => ContractVersion == CurrentContractVersion && TaskId != Guid.Empty && DispatchGeneration > 0;
}

public sealed record CollectionDispatchTaskReference(Guid TaskId, long DispatchGeneration)
{
    public bool IsSupported() => TaskId != Guid.Empty && DispatchGeneration > 0;
}

public sealed record CollectionDispatchCompatibilityKey(string Provider, CollectionDefinitionId Definition,
    DateOnly? EffectiveDate, CollectionLane Lane,
    CollectionDispatchGroupKind GroupKind = CollectionDispatchGroupKind.Definition, string? GroupKey = null)
{
    public bool IsSupported() => !string.IsNullOrWhiteSpace(Provider)
        && (GroupKind == CollectionDispatchGroupKind.Definition
            ? !string.IsNullOrWhiteSpace(Definition.Value)
            : !string.IsNullOrWhiteSpace(GroupKey));
}

public sealed record CollectionDispatchEnvelope(Guid EnvelopeId, CollectionDispatchCompatibilityKey Compatibility,
    IReadOnlyList<CollectionDispatchTaskReference> Tasks, int ContractVersion = 2)
{
    public const int CurrentContractVersion = 2;

    public bool IsSupported()
        => ContractVersion is 1 or CurrentContractVersion
           && EnvelopeId != Guid.Empty
           && Compatibility is not null
           && Compatibility.IsSupported()
           && (ContractVersion != 1 || Compatibility.GroupKind == CollectionDispatchGroupKind.Definition)
           && Tasks is { Count: > 0 }
           && Tasks.All(x => x is not null && x.IsSupported())
           && Tasks.Select(x => x.TaskId).Distinct().Count() == Tasks.Count;
}
public sealed record PendingCollectionDispatch(Guid OutboxId, CollectionTaskNotification Notification,
    ResourceKey Resource, CollectionDefinitionId Definition, DateOnly? EffectiveDate,
    CollectionLane Lane, int Priority, DateTimeOffset AvailableAt, DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string>? Attributes = null);
public sealed record CollectionTaskSummary(Guid TaskId, ResourceKey Resource, CollectionDefinitionId Definition,
    CollectionTaskStatus Status, CollectionLane Lane, int Priority, int RequestedRevision,
    DateTimeOffset AvailableAt, int AttemptCount,
    IReadOnlyDictionary<string, string>? Metadata = null, CollectionReason? Reason = null);

public sealed record CollectionTaskQuery(
    IReadOnlyCollection<CollectionTaskStatus>? Statuses = null,
    CollectionResourceType? ResourceType = null,
    string? Provider = null,
    string? DefinitionId = null,
    CollectionLane? Lane = null,
    string? Search = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? ErrorSearch = null,
    int Page = 1,
    int PageSize = 50,
    bool ActionableOnly = false,
    bool LatestOnly = false);

public sealed record CollectionTaskPage(int TotalCount, int Page, int PageSize,
    IReadOnlyList<CollectionTaskSummary> Items);
public sealed record CollectionTaskViewCounts(IReadOnlyDictionary<string, int> Counts);

public sealed record CollectionStateQuery(IReadOnlyCollection<CollectionStateStatus>? Statuses = null,
    CollectionResourceType? ResourceType = null, string? Provider = null, string? DefinitionId = null,
    string? Search = null, int Page = 1, int PageSize = 50);
public sealed record CollectionStatePage(int TotalCount, int Page, int PageSize,
    IReadOnlyList<CollectionStateSnapshot> Items);

public sealed record CollectionProgressSnapshot(
    IReadOnlyDictionary<CollectionResourceType, int> ResourcesByType,
    IReadOnlyDictionary<CollectionStateStatus, int> StatesByStatus,
    IReadOnlyDictionary<CollectionLane, int> ActiveTasksByLane,
    IReadOnlyDictionary<int, int> ActiveTasksByPriority,
    IReadOnlyDictionary<string, int> StatesByDefinition,
    int RetryWaiting,
    IReadOnlyList<CollectionLaneActivity>? LaneActivity = null);

public sealed record CollectionLaneActivity(CollectionLane Lane, int DueReady, int Running,
    DateTimeOffset? LastStartedAt, DateTimeOffset? LastCompletedAt);

public sealed record CollectionReadinessSnapshot(int PendingHorseRequests, int PendingJockeyRequests,
    int PendingRaceResultRequests, int PendingTrainerRequests)
{
    public int TotalPendingRequests => PendingHorseRequests + PendingJockeyRequests
        + PendingRaceResultRequests + PendingTrainerRequests;
}

public sealed record CollectionPipelineState(bool IsPaused, string? Reason, DateTimeOffset? UpdatedAt);
public sealed record PendingCollectionFailureNotification(Guid NotificationId, Guid TaskId,
    ResourceKey Resource, CollectionDefinitionId Definition, CollectionTaskStatus Status,
    string? ErrorCode, string? ErrorMessage, int AttemptCount, DateTimeOffset FailedAt,
    CollectionFailureResolutionStatus ResolutionStatus = CollectionFailureResolutionStatus.Open,
    Guid? RecoveryTaskId = null, DateTimeOffset? RecoveryStartedAt = null, DateTimeOffset? ResolvedAt = null,
    string? SelectedUrl = null, ResourceKey? CanonicalResource = null, string? Selector = null,
    DateTimeOffset? SelectedAt = null);
public sealed record CollectionFailureGroup(string GroupKey, CollectionDefinitionId Definition,
    CollectionTaskStatus Status, string? ErrorCode, string? ErrorMessage, int Count,
    DateTimeOffset FirstFailedAt, DateTimeOffset LastFailedAt,
    IReadOnlyList<Guid> NotificationIds, IReadOnlyList<ResourceKey> SampleResources);
public sealed record CollectionFailureTarget(Guid NotificationId, Guid TaskId, ResourceKey Resource,
    CollectionDefinitionId Definition, CollectionTaskStatus Status, string? ErrorCode, string? ErrorMessage,
    int AttemptCount, DateTimeOffset FailedAt, string? RequestedUrl, string? FinalUrl, int? HttpStatusCode,
    string? PageIdentification, Guid? ExecutionBatchId, string? LambdaRequestId);
public sealed record CollectionFailureGroupPage(CollectionFailureGroup Group, int TotalCount, int Page,
    int PageSize, string? Search, IReadOnlyList<CollectionFailureTarget> Items);
public sealed record CollectionFailureGroupMatch(int MatchingGroupCount,
    IReadOnlyList<PendingCollectionFailureNotification> Notifications);
public sealed record CollectionFailureRecoveryResult(int SelectedCount, int CreatedTaskCount,
    int ReusedTaskCount, IReadOnlyList<Guid> TaskIds);
public sealed record CollectionFailureDismissalResult(int SelectedCount, int DismissedCount,
    int AlreadyClosedCount, bool HasRecoveryConflict = false);
public sealed record SubjectIdentificationCandidateApplication(Guid NotificationId,
    string SelectedName, Uri SelectedUrl, string? Evidence, ResourceKey CanonicalResource,
    CollectionDefinitionId CanonicalDefinition, Guid CanonicalTaskId, string? Selector,
    DateTimeOffset SelectedAt, bool CreatedTask, bool ReusedTask)
{
    // The existing selection row predates durable reservation and keeps this column non-null.
    // Guid.Empty is therefore the persisted, resumable reservation state.
    public bool IsFinalized => CanonicalTaskId != Guid.Empty;
}
public sealed class SubjectIdentificationSelectionConflictException(Guid notificationId)
    : InvalidOperationException($"Subject identification selection for notification '{notificationId}' conflicts with the existing selection.");
public sealed record CollectionRequestSummary(Guid RequestId, int RequestedRevision, CollectionReason Reason,
    DateTimeOffset RequestedAt, string? ExplicitUrl, string? BatchId);
public sealed record CollectionAttemptSummary(Guid AttemptId, Guid TaskId, int AttemptNumber,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, CollectionAttemptResult Result,
    string? ErrorCode, string? ErrorMessage, string? RequestedUrl, string? FinalUrl, int? HttpStatusCode,
    string? PageIdentification = null, Guid? ExecutionBatchId = null, Guid? DispatchEnvelopeId = null,
    string? QueueMessageId = null, string? LambdaRequestId = null, int? BatchTaskOrdinal = null,
    int? BatchTaskCount = null,
    IReadOnlyList<SubjectIdentificationCandidate>? IdentificationCandidates = null);

public sealed record CollectionAttemptStageSummary(Guid StageOutcomeId, Guid AttemptId, string Stage,
    RaceArtifactKind Artifact, CollectionAttemptResult Result, string? ErrorCode,
    string? ErrorMessage, string? RequestedUrl, string? FinalUrl, bool Persisted);
public sealed record CollectionAttemptCorrelation(Guid ExecutionBatchId, Guid DispatchEnvelopeId,
    string QueueMessageId, string? LambdaRequestId, int BatchTaskOrdinal, int BatchTaskCount)
{
    public bool IsSupported() => ExecutionBatchId != Guid.Empty && DispatchEnvelopeId != Guid.Empty
        && !string.IsNullOrWhiteSpace(QueueMessageId) && QueueMessageId.Length <= 256
        && (LambdaRequestId is null || LambdaRequestId.Length <= 256)
        && BatchTaskCount > 0 && BatchTaskOrdinal > 0 && BatchTaskOrdinal <= BatchTaskCount;
}
public sealed record CollectionExecutionBatchTaskSummary(Guid TaskId, ResourceKey Resource,
    CollectionDefinitionId Definition, CollectionTaskStatus Status, CollectionAttemptResult Result,
    int AttemptNumber, int BatchTaskOrdinal, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt);
public sealed record CollectionExecutionBatchDetail(Guid ExecutionBatchId, Guid DispatchEnvelopeId,
    string QueueMessageId, string? LambdaRequestId, int BatchTaskCount, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt, IReadOnlyList<CollectionExecutionBatchTaskSummary> Tasks);
public sealed record CollectionResourceDetail(CollectionStateSnapshot? State,
    IReadOnlyList<ResourceLocationCandidate> Locations, IReadOnlyList<CollectionRequestSummary> Requests,
    IReadOnlyList<CollectionTaskSummary> Tasks, IReadOnlyList<CollectionAttemptSummary> Attempts,
    int RequestTotal = 0, int TaskTotal = 0, int AttemptTotal = 0, int HistoryPage = 1, int HistoryPageSize = 25,
    CollectionTaskSummary? LatestTask = null, int? TaskHistoryPage = null, int? AttemptHistoryPage = null,
    IReadOnlyList<PendingCollectionFailureNotification>? Failures = null,
    IReadOnlyList<RaceArtifactSnapshot>? RaceArtifacts = null,
    RaceSchedulingEvidence? RaceEvidence = null,
    IReadOnlyList<CollectionAttemptStageSummary>? StageOutcomes = null,
    CollectionOriginSummaryDto? Origin = null)
{
    public int RequestHistoryPage => HistoryPage;
    public int EffectiveTaskHistoryPage => TaskHistoryPage ?? HistoryPage;
    public int EffectiveAttemptHistoryPage => AttemptHistoryPage ?? HistoryPage;
}
public sealed record CollectionWatchdogResult(int ReclaimedLeases, int RedispatchedTasks, int DeadLetteredTasks);
public sealed record BackfillBatchSnapshot(string BatchId, DateOnly From, DateOnly To,
    int ExpectedDiscoveryDays, int RegisteredDiscoveryDays, int Pending, int Running,
    int Succeeded, int Failed, IReadOnlyList<BackfillHole> Holes, DateTimeOffset CreatedAt,
    DateTimeOffset? ExpansionCompletedAt);
public sealed record BackfillHole(ResourceKey Resource, CollectionDefinitionId Definition,
    CollectionTaskStatus Status, string? ErrorCode, string? ErrorMessage);
public sealed record RacePeriodRecollectionPreview(DateOnly From, DateOnly To, int InclusiveDays,
    string Provider);
public sealed record RacePeriodRecollectionReceipt(BackfillBatchSnapshot Batch, int TasksCreated,
    int TasksReused);
public sealed record CollectionInitializationSeed(ResourceKey Resource, CollectionDefinitionId Definition,
    int AppliedRevision, DateTimeOffset CollectedAt, DateOnly? EffectiveDate,
    IReadOnlyDictionary<string, string> Attributes, Uri? SourceUrl = null, bool IsComplete = true);
public sealed record CollectionInitializationReport(bool DryRun, int Examined, int ResourcesAdded,
    int StatesAdded, int LocationsAdded, IReadOnlyList<string> BackfillMonths);
