namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record JobOperationAuditReadModel(string AuditId, string Operation, AgentJobStatus PreviousStatus,
    AgentJobStatus NewStatus, string ActorId, string? Reason, DateTimeOffset CreatedAt);
