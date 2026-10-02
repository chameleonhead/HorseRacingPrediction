namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionProgressSnapshotDto(
    IReadOnlyDictionary<CollectionResourceType, int> ResourcesByType,
    IReadOnlyDictionary<CollectionStateStatus, int> StatesByStatus,
    IReadOnlyDictionary<CollectionLane, int> ActiveTasksByLane,
    IReadOnlyDictionary<int, int> ActiveTasksByPriority,
    IReadOnlyDictionary<string, int> StatesByDefinition,
    int RetryWaiting,
    IReadOnlyList<CollectionLaneActivityDto> LaneActivity);

public sealed record CollectionLaneActivityDto(CollectionLane Lane, int DueReady, int Running,
    DateTimeOffset? LastStartedAt, DateTimeOffset? LastCompletedAt);
