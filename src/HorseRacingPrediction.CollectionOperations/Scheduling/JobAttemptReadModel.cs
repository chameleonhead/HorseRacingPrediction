namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record JobAttemptReadModel(int AttemptNumber, AgentJobStatus Status, string? Error, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);
