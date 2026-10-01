namespace HorseRacingPrediction.Contracts.Collection;

public sealed record RaceEntryOwnerRepairBatchDto(string BatchId, int TargetCount, int TasksCreated,
    IReadOnlyList<Guid> TaskIds);
