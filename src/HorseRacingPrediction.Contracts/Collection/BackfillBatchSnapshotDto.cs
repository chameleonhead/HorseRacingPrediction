namespace HorseRacingPrediction.Contracts.Collection;

public sealed record BackfillBatchSnapshotDto(string BatchId, DateOnly From, DateOnly To,
    int ExpectedDiscoveryDays, int RegisteredDiscoveryDays, int Pending, int Running, int Succeeded,
    int Failed, IReadOnlyList<BackfillHoleDto> Holes, DateTimeOffset CreatedAt,
    DateTimeOffset? ExpansionCompletedAt, BackfillBatchRecoveryDto? Recovery = null);
