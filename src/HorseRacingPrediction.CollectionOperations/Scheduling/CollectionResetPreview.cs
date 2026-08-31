namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record CollectionResetPreview(
    IReadOnlyDictionary<AgentJobStatus, int> JobsByStatus,
    int PendingOutboxCount,
    IReadOnlyDictionary<string, long> TableCounts);
