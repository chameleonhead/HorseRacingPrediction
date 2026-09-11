using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionPlanningScheduler : BackgroundService
{
    private readonly CollectionPlatformStore _store;

    public CollectionPlanningScheduler(CollectionPlatformStore store)
    {
        _store = store;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var planningBucketHour = now.Hour / 3 * 3;
            var bucket = new DateTimeOffset(now.Year, now.Month, now.Day, planningBucketHour, 0, 0, TimeSpan.Zero);
            await _store.RequestAsync(new(ResourceType.Race, "JRA", $"discovery:{bucket:yyyyMMddHH}"),
                new("race-discovery"), 1, CollectionReason.Discovery, now,
                CollectionLane.Realtime, (int)CollectionPriority.High,
                effectiveDate: DateOnly.FromDateTime(now.UtcDateTime),
                cancellationToken: stoppingToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
