namespace HorseRacingPrediction.Contracts.Collection;

public sealed record SearchCollectionStatesRequest(string? Statuses = null,
    CollectionResourceType? ResourceType = null, string? Provider = null, string? DefinitionId = null,
    string? Search = null, int? Page = null, int? PageSize = null);
