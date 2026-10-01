namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionOperationsDashboardDto(CollectionProgressSnapshotDto Progress,
    IReadOnlyList<CollectionFailureGroupDto> Failures, IReadOnlyList<BackfillBatchSnapshotDto> Backfills,
    DateTimeOffset GeneratedAt);
