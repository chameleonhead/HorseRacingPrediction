namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionBatchResourceStatusDto(CollectionResourceKeyDto Resource, int RequestedRevision,
    CollectionTaskStatus? LatestTaskStatus, CollectionStateStatus? StateStatus, int AppliedRevision,
    int RequiredRevision);
