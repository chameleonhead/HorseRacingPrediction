namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CreateBackfillBatchInputDto(int Year, int Month, string Provider = "JRA", string? BatchId = null);
