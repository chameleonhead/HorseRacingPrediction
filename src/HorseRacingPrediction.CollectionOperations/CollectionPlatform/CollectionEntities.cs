namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed class CollectionResourceEntity
{
    public long ResourcePk { get; set; }
    public ResourceType Type { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public DateOnly? EffectiveDate { get; set; }
    public string AttributesJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CollectionDefinitionEntity
{
    public string DefinitionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ResourceType ResourceType { get; set; }
    public int CurrentRevision { get; set; }
    public bool Enabled { get; set; }
}

public sealed class CollectionRevisionEntity
{
    public string DefinitionId { get; set; } = string.Empty;
    public int Revision { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool MayRequireRecollection { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CollectionRevisionImpactEntity
{
    public long ImpactId { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int Revision { get; set; }
    public RevisionImpactScopeType ScopeType { get; set; }
    public string ScopePayload { get; set; } = string.Empty;
}

public sealed class CollectionStateEntity
{
    public long ResourcePk { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int AppliedRevision { get; set; }
    public int RequiredRevision { get; set; }
    public DateTimeOffset? LastCollectedAt { get; set; }
    public DateTimeOffset? NextCollectionAt { get; set; }
    public CollectionStateStatus Status { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CollectionRequestEntity
{
    public Guid RequestId { get; set; }
    public long ResourcePk { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int RequestedRevision { get; set; }
    public CollectionReason Reason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string? ExplicitUrl { get; set; }
    public string? BatchId { get; set; }
}

public sealed class CollectionTaskEntity
{
    public Guid TaskId { get; set; }
    public Guid RequestId { get; set; }
    public long ResourcePk { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int RequestedRevision { get; set; }
    public CollectionTaskStatus Status { get; set; }
    public CollectionLane Lane { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public long DispatchGeneration { get; set; }
    public int AttemptCount { get; set; }
}

public sealed class CollectionActiveTaskEntity
{
    public long ResourcePk { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public Guid TaskId { get; set; }
}

public sealed class CollectionAttemptEntity
{
    public Guid AttemptId { get; set; }
    public Guid TaskId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public CollectionAttemptResult Result { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RequestedUrl { get; set; }
    public string? FinalUrl { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? PageIdentification { get; set; }
}

public sealed class ResourceLocationEntity
{
    public long LocationId { get; set; }
    public long ResourcePk { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public ResourceLocationSource Source { get; set; }
    public ResourceLocationStatus Status { get; set; }
    public DateTimeOffset DiscoveredAt { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public DateTimeOffset? LastFailedAt { get; set; }
    public string? LastFailureCode { get; set; }
}

public sealed class CollectionDispatchOutboxEntity
{
    public Guid OutboxId { get; set; }
    public Guid TaskId { get; set; }
    public long DispatchGeneration { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
