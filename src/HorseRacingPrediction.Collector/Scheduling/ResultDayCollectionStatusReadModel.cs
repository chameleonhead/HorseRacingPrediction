namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record ResultDayCollectionStatusReadModel(
    string ProviderType,
    DateOnly TargetDate,
    ResultDayCollectionState Status,
    int ExpectedRaceCount,
    int CompletedRaceCount,
    string? IncompleteReason,
    DateTimeOffset? LastCompletedAt,
    DateTimeOffset? RetryAfter,
    string? LastError,
    DateTimeOffset UpdatedAt);