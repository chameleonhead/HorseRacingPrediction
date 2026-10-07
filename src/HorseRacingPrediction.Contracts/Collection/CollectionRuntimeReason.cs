namespace HorseRacingPrediction.Contracts.Collection;

public enum CollectionRuntimeReason
{
    Paused = 0,
    NoDueWork = 1,
    CapacityFull = 2,
    RepairHeld = 3,
    BudgetExhausted = 4,
    Error = 5,
}
