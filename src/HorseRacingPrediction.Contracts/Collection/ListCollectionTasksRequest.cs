namespace HorseRacingPrediction.Contracts.Collection;

public sealed record ListCollectionTasksRequest(CollectionTaskStatus? Status = null, int? Limit = null,
    string? Statuses = null, CollectionResourceType? ResourceType = null, string? Provider = null,
    string? DefinitionId = null, CollectionLane? Lane = null, string? Search = null,
    string? ErrorSearch = null, DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null,
    bool? ActionableOnly = null, bool? LatestOnly = null, int? Page = null, int? PageSize = null);
