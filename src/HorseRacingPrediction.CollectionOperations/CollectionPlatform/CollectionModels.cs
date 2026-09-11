namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public enum ResourceType
{
    Race,
    RaceCard,
    RaceOdds,
    RaceResult,
    Horse,
    Jockey,
    Trainer,
}

public readonly record struct ResourceKey(ResourceType Type, string Provider, string Id)
{
    public ResourceKey Normalize() => new(Type, Provider.Trim().ToUpperInvariant(), Id.Trim());
}

public readonly record struct CollectionDefinitionId(string Value)
{
    public override string ToString() => Value;
}

public enum CollectionReason
{
    Initial,
    Backfill,
    Discovery,
    ScheduledRefresh,
    DefinitionChanged,
    ManualRefresh,
    Recovery,
}

public enum CollectionStateStatus
{
    Unknown,
    Pending,
    Collecting,
    Current,
    RefreshDue,
    Stale,
    Failed,
    Unavailable,
}

public enum CollectionTaskStatus
{
    Pending,
    Ready,
    Running,
    RetryWaiting,
    WaitingDiscovery,
    Succeeded,
    Failed,
    Cancelled,
    DeadLetter,
}

public enum CollectionAttemptResult
{
    Running,
    Succeeded,
    TransientFailure,
    PermanentFailure,
    ResourceNotFound,
    ResourceNotYetAvailable,
    ParseFailure,
    ValidationFailure,
    UnexpectedPage,
    AccessLimited,
    Cancelled,
}

public enum CollectionLane { Realtime, Normal, Background }

public enum CollectionPriority
{
    Background = 10,
    Low = 30,
    Normal = 50,
    High = 70,
    Critical = 100,
}

public enum RevisionImpactScopeType { All, SpecificResources, DateRange, NamedCondition }
public enum ResourceLocationSource { Explicit, Generated, Discovered, Redirected, Manual }
public enum ResourceLocationStatus { Unknown, Active, Suspect, Invalid }

public sealed record CollectionSchedule(bool ShouldCollect, DateTimeOffset? NextCollectionAt,
    CollectionPriority Priority, CollectionLane Lane, string Reason);

public interface ICollectionSchedulePolicy
{
    CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now);
}

public sealed record CollectionStateSnapshot(ResourceKey Resource, CollectionDefinitionId Definition,
    int AppliedRevision, int RequiredRevision, DateTimeOffset? LastCollectedAt,
    DateTimeOffset? NextCollectionAt, CollectionStateStatus Status);

public sealed record CollectionRequestReceipt(Guid RequestId, Guid TaskId, bool CreatedTask);

public sealed record LeasedCollectionTask(Guid TaskId, Guid RequestId, ResourceKey Resource,
    CollectionDefinitionId Definition, int RequestedRevision, CollectionReason Reason,
    CollectionLane Lane, int Priority, string LeaseToken, DateTimeOffset LeaseExpiresAt,
    DateOnly? EffectiveDate, IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<ResourceLocationCandidate>? Locations = null);

public sealed record CollectionAttemptCompletion(CollectionAttemptResult Result, string? ErrorCode = null,
    string? ErrorMessage = null, Uri? RequestedUrl = null, Uri? FinalUrl = null,
    int? HttpStatusCode = null, string? PageIdentification = null,
    DateTimeOffset? RetryAt = null, DateTimeOffset? NextCollectionAt = null);

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
    ResourceLocationStatus Status, DateTimeOffset? LastVerifiedAt);

public sealed record CollectionTaskNotification(Guid TaskId, long DispatchGeneration);
public sealed record PendingCollectionDispatch(Guid OutboxId, CollectionTaskNotification Notification,
    CollectionLane Lane, int Priority, DateTimeOffset AvailableAt, DateTimeOffset CreatedAt);
public sealed record CollectionTaskSummary(Guid TaskId, ResourceKey Resource, CollectionDefinitionId Definition,
    CollectionTaskStatus Status, CollectionLane Lane, int Priority, int RequestedRevision,
    DateTimeOffset AvailableAt, int AttemptCount);

public sealed record CollectionTaskQuery(
    IReadOnlyCollection<CollectionTaskStatus>? Statuses = null,
    ResourceType? ResourceType = null,
    string? Provider = null,
    string? DefinitionId = null,
    CollectionLane? Lane = null,
    string? Search = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    int Page = 1,
    int PageSize = 50);

public sealed record CollectionTaskPage(int TotalCount, int Page, int PageSize,
    IReadOnlyList<CollectionTaskSummary> Items);

public sealed record CollectionProgressSnapshot(
    IReadOnlyDictionary<ResourceType, int> ResourcesByType,
    IReadOnlyDictionary<CollectionStateStatus, int> StatesByStatus,
    IReadOnlyDictionary<CollectionLane, int> ActiveTasksByLane,
    IReadOnlyDictionary<int, int> ActiveTasksByPriority,
    IReadOnlyDictionary<string, int> StatesByDefinition,
    int RetryWaiting);

public sealed record CollectionReadinessSnapshot(int PendingHorseRequests, int PendingJockeyRequests,
    int PendingRaceResultRequests, int PendingTrainerRequests)
{
    public int TotalPendingRequests => PendingHorseRequests + PendingJockeyRequests
        + PendingRaceResultRequests + PendingTrainerRequests;
}

public sealed record CollectionPipelineState(bool IsPaused, string? Reason, DateTimeOffset? UpdatedAt);
public sealed record PendingCollectionFailureNotification(Guid NotificationId, Guid TaskId,
    ResourceKey Resource, CollectionDefinitionId Definition, CollectionTaskStatus Status,
    string? ErrorCode, string? ErrorMessage, int AttemptCount, DateTimeOffset FailedAt);
public sealed record CollectionFailureGroup(string GroupKey, CollectionDefinitionId Definition,
    CollectionTaskStatus Status, string? ErrorCode, string? ErrorMessage, int Count,
    DateTimeOffset FirstFailedAt, DateTimeOffset LastFailedAt,
    IReadOnlyList<Guid> NotificationIds, IReadOnlyList<ResourceKey> SampleResources);
public sealed record CollectionFailureRecoveryResult(int SelectedCount, int CreatedTaskCount,
    int ReusedTaskCount, IReadOnlyList<Guid> TaskIds);
public sealed record CollectionRequestSummary(Guid RequestId, int RequestedRevision, CollectionReason Reason,
    DateTimeOffset RequestedAt, string? ExplicitUrl, string? BatchId);
public sealed record CollectionAttemptSummary(Guid AttemptId, Guid TaskId, int AttemptNumber,
    DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, CollectionAttemptResult Result,
    string? ErrorCode, string? ErrorMessage, string? RequestedUrl, string? FinalUrl, int? HttpStatusCode);
public sealed record CollectionResourceDetail(CollectionStateSnapshot? State,
    IReadOnlyList<ResourceLocationCandidate> Locations, IReadOnlyList<CollectionRequestSummary> Requests,
    IReadOnlyList<CollectionTaskSummary> Tasks, IReadOnlyList<CollectionAttemptSummary> Attempts);
public sealed record CollectionWatchdogResult(int ReclaimedLeases, int RedispatchedTasks, int DeadLetteredTasks);
public sealed record BackfillBatchSnapshot(string BatchId, DateOnly From, DateOnly To,
    int ExpectedDiscoveryDays, int RegisteredDiscoveryDays, int Pending, int Running,
    int Succeeded, int Failed, IReadOnlyList<BackfillHole> Holes, DateTimeOffset CreatedAt,
    DateTimeOffset? ExpansionCompletedAt);
public sealed record BackfillHole(ResourceKey Resource, CollectionDefinitionId Definition,
    CollectionTaskStatus Status, string? ErrorCode, string? ErrorMessage);
public sealed record CollectionInitializationSeed(ResourceKey Resource, CollectionDefinitionId Definition,
    int AppliedRevision, DateTimeOffset CollectedAt, DateOnly? EffectiveDate,
    IReadOnlyDictionary<string, string> Attributes, Uri? SourceUrl = null);
public sealed record CollectionInitializationReport(bool DryRun, int Examined, int ResourcesAdded,
    int StatesAdded, int LocationsAdded, IReadOnlyList<string> BackfillMonths);
