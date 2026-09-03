namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record AcquiredProcessingJob(string JobId, string DeduplicationKey, string Payload);
