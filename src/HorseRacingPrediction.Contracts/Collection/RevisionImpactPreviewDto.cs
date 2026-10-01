namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RevisionImpactPreviewDto(CollectionDefinitionIdDto Definition, int Revision,
    RevisionImpactDto Impact, int TotalCandidates, IReadOnlyList<CollectionResourceKeyDto> AffectedResources);
