namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionStatePageDto(int TotalCount, int Page, int PageSize,
    IReadOnlyList<CollectionStateSnapshotDto> Items);
