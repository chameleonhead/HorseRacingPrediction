namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionProgressSnapshotDto(
    IReadOnlyDictionary<CollectionResourceType, int> ResourcesByType,
    IReadOnlyDictionary<CollectionStateStatus, int> StatesByStatus,
    IReadOnlyDictionary<CollectionLane, int> ActiveTasksByLane,
    IReadOnlyDictionary<int, int> ActiveTasksByPriority,
    IReadOnlyDictionary<string, int> StatesByDefinition,
    int RetryWaiting);
