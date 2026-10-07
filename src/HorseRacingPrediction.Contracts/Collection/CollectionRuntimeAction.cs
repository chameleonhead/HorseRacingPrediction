namespace HorseRacingPrediction.Contracts.Collection;

public enum CollectionRuntimeAction
{
    Dispatcher = 0,
    DiscoveryPlanner = 1,
    RefreshPlanner = 2,
    TaskLeaseRecovery = 3,
    BackfillRecovery = 4,
    DeadLetterReconciliation = 5,
    Alerts = 6,
    MetricDelivery = 7,
}
