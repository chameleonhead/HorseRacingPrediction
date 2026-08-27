using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionTaskOutboxDispatcher : BackgroundService
{
    private readonly ProcessingStateStore _store;
    private readonly ICollectionTaskQueue _queue;
    private readonly CollectionQueueOptions _options;
    private readonly ILogger<CollectionTaskOutboxDispatcher> _logger;

    public CollectionTaskOutboxDispatcher(
        ProcessingStateStore store,
        ICollectionTaskQueue queue,
        IOptions<CollectionQueueOptions> options,
        ILogger<CollectionTaskOutboxDispatcher> logger)
    {
        _store = store;
        _queue = queue;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Collection SQS dispatcher is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.DispatchIntervalSeconds)), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dispatches = await _store.GetPendingCollectionTaskDispatchesAsync(
            now,
            Math.Max(1, _options.DispatchBatchSize),
            cancellationToken).ConfigureAwait(false);

        foreach (var dispatch in dispatches)
        {
            try
            {
                await _queue.SendAsync(dispatch.Notification, cancellationToken).ConfigureAwait(false);
                await _store.MarkCollectionTaskDispatchedAsync(dispatch.OutboxId, DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Collection task dispatch failed. OutboxId={OutboxId}", dispatch.OutboxId);
                await _store.MarkCollectionTaskDispatchFailedAsync(
                    dispatch.OutboxId,
                    DateTimeOffset.UtcNow,
                    ex.Message,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
