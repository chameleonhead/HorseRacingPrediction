namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CreateCollectionTaskInputDto(string Mode, CollectionResourceTaskInputDto? Resource = null,
    CollectionSourceUrlTaskInputDto? SourceUrl = null);
