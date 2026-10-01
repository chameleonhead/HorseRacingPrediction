namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionTaskViewCountsDto(IReadOnlyDictionary<string, int> Counts);
