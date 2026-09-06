using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionTaskOutboxDispatcher : BackgroundService
{
    private readonly ProcessingStateStore _store;
    private readonly ICollectionTaskQueue _queue;
    private readonly CollectionQueueOptions _options;
    private readonly CollectionMaintenanceState _maintenance;
    private readonly CollectionQueueCircuitBreakerState _circuitBreaker;
    private readonly ILogger<CollectionTaskOutboxDispatcher> _logger;

    public CollectionTaskOutboxDispatcher(
        ProcessingStateStore store,
        ICollectionTaskQueue queue,
        IOptions<CollectionQueueOptions> options,
        CollectionQueueCircuitBreakerState circuitBreaker,
        ILogger<CollectionTaskOutboxDispatcher> logger)
        : this(store, queue, options, new CollectionMaintenanceState(), circuitBreaker, logger)
    {
    }

    public CollectionTaskOutboxDispatcher(
        ProcessingStateStore store,
        ICollectionTaskQueue queue,
        IOptions<CollectionQueueOptions> options,
        CollectionMaintenanceState maintenance,
        CollectionQueueCircuitBreakerState circuitBreaker,
        ILogger<CollectionTaskOutboxDispatcher> logger)
    {
        _store = store;
        _queue = queue;
        _options = options.Value;
        _maintenance = maintenance;
        _circuitBreaker = circuitBreaker;
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
        if (_maintenance.IsActive) return;
        // DLQ滞留によりCollectionJobWatchdogServiceがサーキットブレーカーをトリップしている間は、
        // 新規ジョブも含めて一切SQSへ送出しない。実行基盤側の恒常的な失敗が解消されるまで、
        // 送っても失敗するメッセージでコストを積み増さないため。
        if (_circuitBreaker.IsTripped)
        {
            _logger.LogDebug("Collection task dispatch skipped: circuit breaker is tripped (DLQ backlog).");
            return;
        }
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
