namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RevisionRecollectionExpansionDto(CollectionDefinitionIdDto Definition, int Revision,
    string BatchId, int Affected, int RequestsCreated, int ExistingRequests);
