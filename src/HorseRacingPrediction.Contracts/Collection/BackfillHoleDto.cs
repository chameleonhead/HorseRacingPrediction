namespace HorseRacingPrediction.Contracts.Collection;

public sealed record BackfillHoleDto(CollectionResourceKeyDto Resource, CollectionDefinitionIdDto Definition,
    CollectionTaskStatus Status, string? ErrorCode, string? ErrorMessage);
