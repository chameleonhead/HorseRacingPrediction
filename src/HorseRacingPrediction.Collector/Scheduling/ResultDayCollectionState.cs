namespace HorseRacingPrediction.Collector.Scheduling;

public enum ResultDayCollectionState
{
    NotStarted = 0,
    Discovering = 1,
    Ready = 2,
    Running = 3,
    Partial = 4,
    Incomplete = 5,
    Complete = 6,
    RetryScheduled = 7,
    DeadLetter = 8,
}