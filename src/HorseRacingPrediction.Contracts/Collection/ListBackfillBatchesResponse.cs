namespace HorseRacingPrediction.Contracts.Collection;

public sealed record ListBackfillBatchesResponse(IReadOnlyList<BackfillBatchSnapshotDto> Batches);
