namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionFailureGroupPageDto(CollectionFailureGroupDto Group, int TotalCount, int Page,
    int PageSize, string? Search, IReadOnlyList<CollectionFailureTargetDto> Items);
