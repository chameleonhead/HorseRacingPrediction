using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HorseRacingPrediction.Api.CollectionController;

public interface ICollectionPlatformTaskQueue
{
    Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope, CancellationToken cancellationToken);
    Task<IReadOnlyList<CollectionPlatformDeadLetterMessage>> ReceiveDeadLetterMessagesAsync(int maxMessages,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CollectionPlatformDeadLetterMessage>>([]);
    Task DeleteDeadLetterMessageAsync(string receiptHandle, CancellationToken cancellationToken) => Task.CompletedTask;
}
public sealed record CollectionQueueSendReceipt(string? MessageId);

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
            Math.Max(100, _options.DispatchBatchSize * 20), cancellationToken).ConfigureAwait(false))
            .Where(x => x.Definition.Value == "race-odds" || _options.AggregationDelayMilliseconds <= 0
                || x.CreatedAt <= now.AddMilliseconds(-_options.AggregationDelayMilliseconds)).ToList();
        for (var sent = 0; sent < Math.Max(1, _options.DispatchBatchSize) && remaining.Count > 0; sent++)
        {
            var selected = _allocator.Select(remaining.Select(x => new FairCollectionCandidate(
                x.Notification.TaskId, x.Lane, x.Priority, x.AvailableAt, x.CreatedAt)), now);
            if (selected is null) break;
            var item = remaining.Single(x => x.Notification.TaskId == selected.TaskId);
            var definitionLimit = _options.DefinitionMaxTasks.GetValueOrDefault(item.Definition.Value,
                _options.EnvelopeMaxTasks);
            var maxTasks = Math.Max(1, Math.Min(_options.EnvelopeMaxTasks, definitionLimit));
            var group = remaining.Where(x => IsCompatible(item, x))
                .OrderByDescending(x => x.Priority).ThenBy(x => x.CreatedAt).ThenBy(x => x.Notification.TaskId)
                .Take(maxTasks).ToList();
            var envelopeId = Guid.NewGuid();
            var envelope = CreateEnvelope(envelopeId, item, group);
            while (group.Count > 1 && JsonSerializer.SerializeToUtf8Bytes(envelope).Length
                   > Math.Clamp(_options.EnvelopeMaxPayloadBytes, 1, 256_000))
            {
                group.RemoveAt(group.Count - 1);
                envelope = CreateEnvelope(envelopeId, item, group);
            }
            foreach (var grouped in group) remaining.Remove(grouped);
            var reservationToken = Guid.NewGuid().ToString("N");
            try
            {
                if (!await store.TryReserveDispatchesAsync(group.Select(x => x.OutboxId).ToArray(), reservationToken,
                        envelopeId, now, TimeSpan.FromSeconds(Math.Max(10, _options.OutboxReservationSeconds)),
                        cancellationToken).ConfigureAwait(false))
                    continue;
                var receipt = await queue.SendAsync(envelope, cancellationToken).ConfigureAwait(false);
                if (!await store.MarkDispatchedAsync(group.Select(x => x.OutboxId).ToArray(), reservationToken,
                        envelopeId, receipt.MessageId, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false))
                    logger.LogWarning("Collection envelope was sent but its outbox reservation could not be finalized. EnvelopeId={EnvelopeId}", envelopeId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Resource collection envelope dispatch failed. EnvelopeId={EnvelopeId}", envelopeId);
            }
        }
    }

    private static bool IsCompatible(PendingCollectionDispatch first, PendingCollectionDispatch candidate)
        => string.Equals(first.Resource.Provider, candidate.Resource.Provider, StringComparison.OrdinalIgnoreCase)
           && first.Definition == candidate.Definition
           && first.EffectiveDate == candidate.EffectiveDate
           && first.Lane == candidate.Lane;

    private static CollectionDispatchEnvelope CreateEnvelope(Guid envelopeId, PendingCollectionDispatch first,
        IReadOnlyList<PendingCollectionDispatch> group) => new(envelopeId,
        new(first.Resource.Provider, first.Definition, first.EffectiveDate, first.Lane),
        group.Select(x => new CollectionDispatchTaskReference(x.Notification.TaskId,
            x.Notification.DispatchGeneration)).ToArray());
}
