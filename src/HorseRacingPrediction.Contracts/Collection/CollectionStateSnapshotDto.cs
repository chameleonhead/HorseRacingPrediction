namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionStateSnapshotDto(CollectionResourceKeyDto Resource, CollectionDefinitionIdDto Definition,
    int AppliedRevision, int RequiredRevision, DateTimeOffset? LastCollectedAt,
    DateTimeOffset? NextCollectionAt, CollectionStateStatus Status,
    IReadOnlyList<RaceArtifactSnapshotDto>? RaceArtifacts = null);
