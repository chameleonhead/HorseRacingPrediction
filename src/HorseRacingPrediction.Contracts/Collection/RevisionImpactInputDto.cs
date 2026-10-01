namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RevisionImpactInputDto(RevisionImpactScopeType ScopeType,
    IReadOnlyList<CollectionResourceKeyDto>? Resources = null, DateOnly? From = null, DateOnly? To = null,
    string? NamedCondition = null);
