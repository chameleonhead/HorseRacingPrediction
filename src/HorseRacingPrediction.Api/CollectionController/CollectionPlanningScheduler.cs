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
            var resource = new ResourceKey(ResourceType.Race, "JRA", $"discovery:{bucket:yyyyMMddHH}");
            var definition = new CollectionDefinitionId("race-discovery");
            // A planning bucket is a logical resource. Once registered, its state is the durable
            // evidence that this bucket was planned; terminal tasks must not be recreated every minute.
            if (await _store.GetStateAsync(resource, definition, stoppingToken).ConfigureAwait(false) is null)
                await _store.RequestAsync(resource, definition, 1, CollectionReason.Discovery, now,
                    CollectionLane.Realtime, (int)CollectionPriority.High,
                    effectiveDate: DateOnly.FromDateTime(now.UtcDateTime),
                    cancellationToken: stoppingToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
