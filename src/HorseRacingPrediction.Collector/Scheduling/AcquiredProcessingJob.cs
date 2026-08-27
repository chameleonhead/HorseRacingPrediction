namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record AcquiredProcessingJob(string DeduplicationKey, string Payload);
