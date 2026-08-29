namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class ProcessingJobEntity
{
    public string JobId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string DeduplicationKey { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string? ParentJobId { get; set; }
    public AgentJobStatus Status { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset FirstQueuedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string? LeaseToken { get; set; }
    public long DispatchGeneration { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
