namespace HorseRacingPrediction.Collector.Scheduling;

/// <summary>Describes the meaning of a job's parent link.</summary>
public enum JobRelationType
{
    /// <summary>The parent produced this job, but does not wait for it.</summary>
    GeneratedBy = 0,

    /// <summary>The parent aggregates this job and waits for its terminal state.</summary>
    AggregatedBy = 1
}
