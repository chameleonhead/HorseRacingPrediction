namespace HorseRacingPrediction.Collector.Scheduling;

public enum CollectionRunMode
{
    Plan,
    Work,
    All
}

public sealed class CollectionRunCoordinator
{
    private readonly ScrapingRegistrationService _registration;
    private readonly CollectionExecutionService _collection;
    private readonly HistoricalDataRequestExecutionService _historical;

    public CollectionRunCoordinator(
        ScrapingRegistrationService registration,
        CollectionExecutionService collection,
        HistoricalDataRequestExecutionService historical)
    {
        _registration = registration;
        _collection = collection;
        _historical = historical;
    }

    public async Task RunOnceAsync(CollectionRunMode mode, CancellationToken cancellationToken = default)
    {
        if (mode is CollectionRunMode.Plan or CollectionRunMode.All)
            await _registration.RunOneCycleAsync(cancellationToken).ConfigureAwait(false);

        if (mode is CollectionRunMode.Work or CollectionRunMode.All)
        {
            await _collection.RunOneCycleAsync(cancellationToken).ConfigureAwait(false);
            await _historical.RunOneCycleAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
