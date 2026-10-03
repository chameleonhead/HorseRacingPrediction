namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionTaskSummaryDto(Guid TaskId, CollectionResourceKeyDto Resource,
    CollectionDefinitionIdDto Definition, CollectionTaskStatus Status, CollectionLane Lane, int Priority,
    int RequestedRevision, DateTimeOffset AvailableAt, int AttemptCount,
    IReadOnlyDictionary<string, string>? Metadata = null, CollectionReason? Reason = null);
