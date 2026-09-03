// JRAサイト再設計（docs/jra-scraping.md）により、旧 JraNavigation/Scrapers.Jra 層は削除された。
// 新しい Jra/ 層に対する再実装までの間、ビルドを通すために一時的に無効化する。
#if false
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
#endif
