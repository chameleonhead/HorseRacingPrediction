namespace HorseRacingPrediction.Contracts.Collection;

public sealed record LeasedCollectionTaskDto(Guid TaskId, Guid RequestId, CollectionResourceKeyDto Resource,
    CollectionDefinitionIdDto Definition, int RequestedRevision, CollectionReason Reason, CollectionLane Lane,
    int Priority, string LeaseToken, DateTimeOffset LeaseExpiresAt, DateOnly? EffectiveDate,
    IReadOnlyDictionary<string, string> Attributes, IReadOnlyList<ResourceLocationCandidateDto>? Locations = null,
    long RaceHoldGeneration = 0, string? EntryAssignmentFingerprint = null);
