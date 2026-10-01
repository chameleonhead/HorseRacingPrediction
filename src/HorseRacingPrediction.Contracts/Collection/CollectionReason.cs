namespace HorseRacingPrediction.Contracts.Collection;

public enum CollectionReason
{
    Initial,
    Backfill,
    Discovery,
    ScheduledRefresh,
    DefinitionChanged,
    ManualRefresh,
    Recovery,
    PeriodRecollection,
}
