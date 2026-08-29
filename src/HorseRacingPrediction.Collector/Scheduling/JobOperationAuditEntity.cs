namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class JobOperationAuditEntity
{
    public string AuditId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public AgentJobStatus PreviousStatus { get; set; }
    public AgentJobStatus NewStatus { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
