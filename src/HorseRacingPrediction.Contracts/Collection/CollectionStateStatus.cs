namespace HorseRacingPrediction.Contracts.Collection;

public enum CollectionStateStatus
{
    Unknown,
    Pending,
    Collecting,
    Current,
    RefreshDue,
    Stale,
    Failed,
    Unavailable,
}
