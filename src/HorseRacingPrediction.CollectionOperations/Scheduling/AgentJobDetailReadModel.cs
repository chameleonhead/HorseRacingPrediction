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
    AgentRelatedJobReadModel? ParentJob,
    JobRelationType? ParentRelationType,
    IReadOnlyList<AgentRelatedJobReadModel> ChildJobs,
    IReadOnlyList<JobOperationAuditReadModel> AuditHistory,
    IReadOnlyList<JobAttemptReadModel> Attempts);

public sealed record AgentRelatedJobReadModel(
    string JobId,
    string JobType,
    string DeduplicationKey,
    JobRelationType RelationType,
    AgentJobStatus Status,
    DateTimeOffset UpdatedAt);
