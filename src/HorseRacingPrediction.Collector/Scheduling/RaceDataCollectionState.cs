namespace HorseRacingPrediction.Collector.Scheduling;

public enum RaceDataCollectionState
{
    Unknown = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    DeadLetter = 4,
}