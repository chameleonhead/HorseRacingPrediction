namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record CollectionTaskNotification(
    string TaskId,
    string JobType,
    string DeduplicationKey,
    long DispatchGeneration = 0);

public sealed record PendingCollectionTaskDispatch(
    string OutboxId,
    CollectionTaskNotification Notification,
    int AttemptCount);

public sealed record LeasedCollectionTask(
    string TaskId,
    string JobType,
    string DeduplicationKey,
    string Payload,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAt);

public sealed class CollectionDispatchOutboxEntity
{
    public string OutboxId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string DeduplicationKey { get; set; } = string.Empty;
    public long DispatchGeneration { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class CollectionDispatchPolicy
{
    public static bool IsDispatchable(string jobType)
        => !string.Equals(jobType, AgentJobType.PredictionExecution, StringComparison.Ordinal);
}
