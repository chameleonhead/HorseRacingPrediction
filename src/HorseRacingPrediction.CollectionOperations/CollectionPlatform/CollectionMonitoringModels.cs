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
    int AttemptCount,
    string? CompatibilityKey = null,
    bool IsDispatchCandidate = false);

public sealed record CollectionMonitoringDispatchSnapshot(
    Guid EnvelopeId,
    Guid TaskId,
    CollectionDefinitionId Definition,
    CollectionLane Lane,
    int Priority,
    DateTimeOffset AvailableAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset DispatchedAt,
    string? CompatibilityKey = null);

public sealed record CollectionRaceFreshnessSnapshot(
    Guid TaskId,
    ResourceKey Resource,
    CollectionTaskStatus Status,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? OfficialStartAt,
    RaceArtifactStatus CardStatus,
    RaceArtifactStatus ResultStatus);

public sealed record CollectionDefinitionFlowSnapshot(
    CollectionDefinitionId Definition,
    CollectionLane Lane,
    string CompatibilityKey,
    int Arrived,
    int Dispatched,
    int Completed,
    int Active,
    DateTimeOffset? OldestActiveAt);

public sealed record CollectionMonitoringSnapshot(
    DateTimeOffset Cutoff,
    CollectionPipelineState Pipeline,
    IReadOnlyList<CollectionMonitoringTaskSnapshot> ActiveTasks,
    IReadOnlyList<CollectionMonitoringDispatchSnapshot> RecentDispatches,
    bool Truncated,
    IReadOnlyList<CollectionRaceFreshnessSnapshot>? RaceFreshness = null,
    IReadOnlyList<CollectionDefinitionFlowSnapshot>? DefinitionFlows = null);

public sealed record CollectionMonitoringBackup(string BackupId, string FileName, DateTimeOffset CreatedAt);
