namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record SubjectCollectionStatus(AgentJobDetailReadModel Job, int Discovered, int Completed,
    int Failed, int Excluded, IReadOnlyList<string> ExclusionReasons);
