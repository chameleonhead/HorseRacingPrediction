namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed record CollectionMonitoringTaskSnapshot(
    Guid TaskId,
    ResourceKey Resource,
    CollectionDefinitionId Definition,
    CollectionTaskStatus Status,
    CollectionLane Lane,
    int Priority,
    DateTimeOffset AvailableAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LeaseExpiresAt,
    int AttemptCount);

public sealed record CollectionMonitoringDispatchSnapshot(
    Guid EnvelopeId,
    Guid TaskId,
    CollectionDefinitionId Definition,
    CollectionLane Lane,
    int Priority,
    DateTimeOffset AvailableAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset DispatchedAt);

public sealed record CollectionMonitoringSnapshot(
    DateTimeOffset Cutoff,
    CollectionPipelineState Pipeline,
    IReadOnlyList<CollectionMonitoringTaskSnapshot> ActiveTasks,
    IReadOnlyList<CollectionMonitoringDispatchSnapshot> RecentDispatches,
    bool Truncated);

public sealed record CollectionMonitoringBackup(string BackupId, string FileName, DateTimeOffset CreatedAt);
