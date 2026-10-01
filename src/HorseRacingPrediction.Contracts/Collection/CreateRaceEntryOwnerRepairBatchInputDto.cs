namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CreateRaceEntryOwnerRepairBatchInputDto(DateOnly Date, IReadOnlyList<string> RaceIds,
    string? BatchId = null);
