namespace HorseRacingPrediction.Contracts.Collection;

public enum CollectionTaskAcquireStatus
{
    Acquired,
    AlreadyTerminal,
    SupersededGeneration,
    ActiveElsewhere,
    RepairHeld,
}
