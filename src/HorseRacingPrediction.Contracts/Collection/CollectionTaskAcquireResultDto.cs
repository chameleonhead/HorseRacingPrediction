namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionTaskAcquireResultDto(CollectionTaskAcquireStatus Status,
    LeasedCollectionTaskDto? Task = null);
