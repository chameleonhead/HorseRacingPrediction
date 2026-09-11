using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.CollectionController;

public interface ICollectionPlatformTaskQueue
{
    Task SendAsync(CollectionTaskNotification notification, CancellationToken cancellationToken);
    Task<IReadOnlyList<CollectionPlatformDeadLetterMessage>> ReceiveDeadLetterMessagesAsync(int maxMessages,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CollectionPlatformDeadLetterMessage>>([]);
    Task DeleteDeadLetterMessageAsync(string receiptHandle, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record CollectionPlatformDeadLetterMessage(string ReceiptHandle, string Body);

public sealed class CollectionPlatformOutboxDispatcher(
    CollectionPlatformStore store,
    ICollectionPlatformTaskQueue queue,
    IOptions<CollectionQueueOptions> options,
    ILogger<CollectionPlatformOutboxDispatcher> logger) : BackgroundService
{
    private readonly CollectionQueueOptions _options = options.Value;
    private readonly CollectionLaneAllocator _allocator = new();

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
        var now = DateTimeOffset.UtcNow;
        var remaining = (await store.GetPendingDispatchesAsync(now,
            Math.Max(100, _options.DispatchBatchSize * 20), cancellationToken).ConfigureAwait(false)).ToList();
        for (var sent = 0; sent < Math.Max(1, _options.DispatchBatchSize) && remaining.Count > 0; sent++)
        {
            var selected = _allocator.Select(remaining.Select(x => new FairCollectionCandidate(
                x.Notification.TaskId, x.Lane, x.Priority, x.AvailableAt, x.CreatedAt)), now);
            if (selected is null) break;
            var item = remaining.Single(x => x.Notification.TaskId == selected.TaskId);
            remaining.Remove(item);
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
