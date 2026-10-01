namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RacePeriodRecollectionReceiptDto(BackfillBatchSnapshotDto Batch, int TasksCreated,
    int TasksReused);
