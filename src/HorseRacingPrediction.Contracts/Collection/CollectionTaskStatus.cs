namespace HorseRacingPrediction.Contracts.Collection;

public enum CollectionTaskStatus
{
    Pending,
    Ready,
    Running,
    RetryWaiting,
    WaitingDiscovery,
    Succeeded,
    Failed,
    Cancelled,
    DeadLetter,
}
