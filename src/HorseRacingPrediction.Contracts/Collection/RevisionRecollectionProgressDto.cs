namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RevisionRecollectionProgressDto(CollectionDefinitionIdDto Definition, int Revision,
    int Affected, int Completed, int Pending, int Failed);
