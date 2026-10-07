namespace HorseRacingPrediction.Contracts.Collection;

public enum CollectionRuntimeState
{
    NotObserved = 0,
    Running = 1,
    Waiting = 2,
    Error = 3,
    Disabled = 4,
}
