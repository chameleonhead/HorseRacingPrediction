namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionFailureGroupDto(string GroupKey, CollectionDefinitionIdDto Definition,
    CollectionTaskStatus Status, string? ErrorCode, string? ErrorMessage, int Count,
    DateTimeOffset FirstFailedAt, DateTimeOffset LastFailedAt, IReadOnlyList<Guid> NotificationIds,
    IReadOnlyList<CollectionResourceKeyDto> SampleResources);
