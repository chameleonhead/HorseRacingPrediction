namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record AgentJobStatusReadModel(
    string JobId,
    string JobType,
    string DeduplicationKey,
    AgentJobStatus Status,
    int Priority,
    int AttemptCount,
    DateTimeOffset FirstQueuedAt,
    DateTimeOffset AvailableAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LeaseExpiresAt,
    string? LastError,
    DateTimeOffset UpdatedAt);