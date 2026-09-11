using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.CollectionController;

public interface ICollectionPlatformTaskQueue
{
    Task SendAsync(CollectionTaskNotification notification, CancellationToken cancellationToken);
}

public sealed class CollectionPlatformOutboxDispatcher(
    CollectionPlatformStore store,
    ICollectionPlatformTaskQueue queue,
    IOptions<CollectionQueueOptions> options,
    ILogger<CollectionPlatformOutboxDispatcher> logger) : BackgroundService
{
    private readonly CollectionQueueOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.DispatchIntervalSeconds)), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        var items = await store.GetPendingDispatchesAsync(DateTimeOffset.UtcNow,
            Math.Max(1, _options.DispatchBatchSize), cancellationToken).ConfigureAwait(false);
        foreach (var item in items)
        {
            try
            {
                await queue.SendAsync(item.Notification, cancellationToken).ConfigureAwait(false);
                await store.MarkDispatchedAsync(item.OutboxId, DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Resource collection dispatch failed. OutboxId={OutboxId}", item.OutboxId);
            }
        }
    }
}
