using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HorseRacingPrediction.Api.CollectionController;

public interface ICollectionPlatformTaskQueue
{
    Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope, CancellationToken cancellationToken);
    Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake, CancellationToken cancellationToken)
        => throw new NotSupportedException("This queue does not support wake-only collection messages.");
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

    internal Func<IReadOnlyList<PendingCollectionDispatch>, CancellationToken, Task>? BeforeReservationAsync { get; set; }

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
        var now = HorseRacingPrediction.Contracts.Time.JstTime.Now();
        await store.ReclaimExpiredExecutionLeasesAsync(now, cancellationToken).ConfigureAwait(false);
        var dispatchState = await store.GetLaneDispatchStateAsync(cancellationToken).ConfigureAwait(false);
        var maxDispatches = Math.Clamp(_options.DispatchBatchSize, 1, 256);
        var scanBudget = Math.Min(4096, Math.Max(64, maxDispatches * 8));
        // Scan a bounded keyset page and resume after the last successfully acquired wake.
        // When the cursor reaches the end, the store wraps to the first due page.
        var remaining = (await store.GetPendingDispatchesAsync(now, scanBudget, cancellationToken,
            _options.AggregationDelayMilliseconds, dispatchState.ScanAvailableAt, dispatchState.ScanOutboxId)
            .ConfigureAwait(false)).ToList();
        var sent = 0;
        var scanned = 0;
        while (sent < maxDispatches && scanned < scanBudget && remaining.Count > 0)
        {
            var selected = _allocator.Select(remaining.Select(x => new FairCollectionCandidate(
                    x.Notification.TaskId, x.Lane, x.Priority, x.AvailableAt, x.CreatedAt, x.OutboxId)), now, dispatchState);
            if (selected is null) break;
            var item = remaining.Single(x => x.Notification.TaskId == selected.TaskId);
            var compatibility = CreateCompatibility(item);
            var maxTasks = GetMaxTasks(compatibility, item);
            var admitted = remaining.Where(x => IsCompatible(item, x))
                .OrderByDescending(x => EffectivePriority(x, now))
                .ThenBy(x => x.AvailableAt).ThenBy(x => x.CreatedAt).ThenBy(x => x.Notification.TaskId)
                .Take(maxTasks);
            var group = admitted
                .OrderBy(x => RouteCourse(x)).ThenBy(x => RouteRaceNumber(x)).ThenBy(x => RouteType(x))
                .ThenByDescending(x => x.Priority).ThenBy(x => x.CreatedAt).ThenBy(x => x.Notification.TaskId)
                .ToList();
            scanned++;
            var envelopeId = Guid.NewGuid();
            var wakeId = Guid.NewGuid();
            var envelope = CreateEnvelope(envelopeId, compatibility, group);
            while (group.Count > 1 && JsonSerializer.SerializeToUtf8Bytes(envelope).Length
                   > Math.Clamp(_options.EnvelopeMaxPayloadBytes, 1, 256_000))
            {
                group.RemoveAt(group.Count - 1);
                envelope = CreateEnvelope(envelopeId, compatibility, group);
            }
            foreach (var grouped in group) remaining.Remove(grouped);
            var reservationToken = Guid.NewGuid().ToString("N");
            try
            {
                if (BeforeReservationAsync is not null)
                    await BeforeReservationAsync(group, cancellationToken).ConfigureAwait(false);
                var reserved = await store.TryReserveDispatchesWithinCapacityAsync(group.Select(x => x.OutboxId).ToArray(),
                        reservationToken, envelopeId, wakeId, now,
                        TimeSpan.FromSeconds(Math.Max(10, _options.OutboxReservationSeconds)),
                        Math.Max(1, _options.MaxInFlightEnvelopes), _options.AggregationDelayMilliseconds,
                        cancellationToken).ConfigureAwait(false);
                if (!reserved)
                {
                    // Another dispatcher may have consumed a lane grant while this instance was selecting.
                    // Reload persistent fairness before selecting from the remaining bounded scan page.
                    dispatchState = await store.GetLaneDispatchStateAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                dispatchState = await store.GetLaneDispatchStateAsync(cancellationToken).ConfigureAwait(false);
                var wake = new CollectionWakeSignal(wakeId, envelopeId, reservationToken);
                CollectionQueueSendReceipt receipt;
                var legacyAdapter = false;
                try { receipt = await queue.SendWakeAsync(wake, cancellationToken).ConfigureAwait(false); }
                catch (NotSupportedException)
                {
                    // Transitional in-process test adapters may still expose the old method. Production
                    // queue adapters implement SendWakeAsync and never put task identifiers on SQS.
                    legacyAdapter = true;
                    receipt = await queue.SendAsync(envelope, cancellationToken).ConfigureAwait(false);
                }
                if (legacyAdapter)
                {
                    if (!await store.MarkDispatchedAsync(group.Select(x => x.OutboxId).ToArray(), reservationToken,
                        envelopeId, receipt.MessageId, HorseRacingPrediction.Contracts.Time.JstTime.Now(), cancellationToken)
                        .ConfigureAwait(false))
                        continue;
                }
                else
                    await store.MarkWakeSentAsync(envelopeId, reservationToken, receipt.MessageId, cancellationToken)
                        .ConfigureAwait(false);
                sent++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Resource collection envelope dispatch failed. EnvelopeId={EnvelopeId}", envelopeId);
            }
        }
    }

    private static bool IsCompatible(PendingCollectionDispatch first, PendingCollectionDispatch candidate)
        => CreateCompatibility(first) is var key
           && CreateCompatibility(candidate) is var other
           && string.Equals(key.Provider, other.Provider, StringComparison.OrdinalIgnoreCase)
           && key.GroupKind == other.GroupKind && string.Equals(key.GroupKey, other.GroupKey, StringComparison.Ordinal)
           && key.EffectiveDate == other.EffectiveDate && key.Lane == other.Lane;

    private static CollectionDispatchEnvelope CreateEnvelope(Guid envelopeId, CollectionDispatchCompatibilityKey compatibility,
        IReadOnlyList<PendingCollectionDispatch> group) => new(envelopeId, compatibility,
        group.Select(x => new CollectionDispatchTaskReference(x.Notification.TaskId,
            x.Notification.DispatchGeneration)).ToArray());

    private static CollectionDispatchCompatibilityKey CreateCompatibility(PendingCollectionDispatch item)
    {
        if (item.EffectiveDate.HasValue && item.Resource.Type is CollectionResourceType.RaceCard or CollectionResourceType.RaceResult or CollectionResourceType.Race)
            return new(item.Resource.Provider, item.Definition, item.EffectiveDate, item.Lane,
                CollectionDispatchGroupKind.RaceDay, item.EffectiveDate.Value.ToString("yyyy-MM-dd"));
        if (item.Resource.Type == CollectionResourceType.Horse
            && item.Attributes?.GetValueOrDefault("weekendPriorityUntil") is { Length: > 0 } weekend)
            return new(item.Resource.Provider, item.Definition, item.EffectiveDate, item.Lane,
                CollectionDispatchGroupKind.WeekendSubjects, weekend);
        return new(item.Resource.Provider, item.Definition, item.EffectiveDate, item.Lane,
            CollectionDispatchGroupKind.Definition, item.Definition.Value);
    }

    private int GetMaxTasks(CollectionDispatchCompatibilityKey key, PendingCollectionDispatch item)
    {
        var groupLimit = key.GroupKind switch
        {
            CollectionDispatchGroupKind.RaceDay => _options.RaceDayMaxTasks,
            CollectionDispatchGroupKind.WeekendSubjects => _options.WeekendSubjectsMaxTasks,
            _ => _options.DefinitionMaxTasks.GetValueOrDefault(item.Definition.Value, _options.EnvelopeMaxTasks),
        };
        return Math.Max(1, groupLimit);
    }

    private static string RouteCourse(PendingCollectionDispatch item)
        => item.Attributes?.GetValueOrDefault("course") ?? string.Empty;
    private static int RouteRaceNumber(PendingCollectionDispatch item)
        => int.TryParse(item.Attributes?.GetValueOrDefault("number"), out var number) ? number : int.MaxValue;
    private static int RouteType(PendingCollectionDispatch item) => item.Resource.Type switch
    {
        CollectionResourceType.Race or CollectionResourceType.RaceCard => 0,
        CollectionResourceType.RaceResult => 1,
        _ => 2,
    };

    private static int EffectivePriority(PendingCollectionDispatch item, DateTimeOffset now)
        => item.Priority + Math.Min(30, Math.Max(0, (int)(now - item.CreatedAt).TotalHours / 6));
}
