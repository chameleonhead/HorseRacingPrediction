namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CreateCollectionRevisionInputDto(int Revision, string Description, RevisionImpactInputDto Impact);
