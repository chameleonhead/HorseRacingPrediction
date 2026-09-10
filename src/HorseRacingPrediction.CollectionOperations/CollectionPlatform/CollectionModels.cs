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
    CollectionLane Lane, int Priority, string LeaseToken, DateTimeOffset LeaseExpiresAt);

public sealed record CollectionAttemptCompletion(CollectionAttemptResult Result, string? ErrorCode = null,
    string? ErrorMessage = null, Uri? RequestedUrl = null, Uri? FinalUrl = null,
    int? HttpStatusCode = null, string? PageIdentification = null,
    DateTimeOffset? RetryAt = null, DateTimeOffset? NextCollectionAt = null);

public sealed record RevisionImpact(RevisionImpactScopeType ScopeType, string ScopePayload);

public sealed record RevisionResourceCandidate(ResourceKey Resource, DateOnly? EffectiveDate,
    IReadOnlyDictionary<string, string> Attributes);

public interface INamedRevisionImpactCondition
{
    string Name { get; }
    bool Matches(RevisionResourceCandidate candidate);
}

public sealed record ResourceLocationCandidate(long LocationId, Uri Url, ResourceLocationSource Source,
    ResourceLocationStatus Status, DateTimeOffset? LastVerifiedAt);

public sealed record CollectionTaskNotification(Guid TaskId, long DispatchGeneration);
public sealed record PendingCollectionDispatch(Guid OutboxId, CollectionTaskNotification Notification);
public sealed record CollectionTaskSummary(Guid TaskId, ResourceKey Resource, CollectionDefinitionId Definition,
    CollectionTaskStatus Status, CollectionLane Lane, int Priority, int RequestedRevision,
    DateTimeOffset AvailableAt, int AttemptCount);
