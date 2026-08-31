namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class JobAttemptEntity
{
    public string AttemptId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public AgentJobStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
