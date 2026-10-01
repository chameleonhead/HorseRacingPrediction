namespace HorseRacingPrediction.Contracts.Collection;

public sealed record AcquireCollectionTaskInputDto(long DispatchGeneration, int LeaseSeconds = 900,
    CollectionAttemptCorrelationDto? Correlation = null);
