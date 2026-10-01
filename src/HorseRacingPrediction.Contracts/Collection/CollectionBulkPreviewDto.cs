namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionBulkPreviewDto(CollectionDefinitionIdDto Definition, int Revision,
    int TargetCount, IReadOnlyList<CollectionResourceKeyDto> Resources);
