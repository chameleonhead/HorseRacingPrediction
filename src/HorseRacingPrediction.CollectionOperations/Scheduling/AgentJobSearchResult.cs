namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record AgentJobSearchResult(
    IReadOnlyList<AgentJobStatusReadModel> Items,
    int TotalCount,
    IReadOnlyList<AgentJobDaySummary> DaySummaries);

public sealed record AgentJobDaySummary(
    string Date,
    int Count,
    int CompletedCount,
    int AttentionCount,
    int WaitingCount);
