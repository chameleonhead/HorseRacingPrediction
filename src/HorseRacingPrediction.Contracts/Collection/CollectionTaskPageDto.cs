namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionTaskPageDto(int TotalCount, int Page, int PageSize,
    IReadOnlyList<CollectionTaskSummaryDto> Items);
