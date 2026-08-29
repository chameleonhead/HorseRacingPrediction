namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record AgentJobDetailReadModel(
    string JobId,
    string JobType,
    string DeduplicationKey,
    string Payload,
    AgentJobStatus Status,
    int Priority,
    int AttemptCount,
    DateTimeOffset FirstQueuedAt,
    DateTimeOffset AvailableAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LeaseExpiresAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<JobOperationAuditReadModel> AuditHistory);
