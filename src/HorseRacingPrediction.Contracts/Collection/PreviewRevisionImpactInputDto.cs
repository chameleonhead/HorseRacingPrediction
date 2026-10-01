namespace HorseRacingPrediction.Contracts.Collection;

public sealed record PreviewRevisionImpactInputDto(string DefinitionId, int Revision, RevisionImpactInputDto Impact);
