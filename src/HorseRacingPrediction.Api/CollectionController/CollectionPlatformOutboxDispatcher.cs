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
        var now = HorseRacingPrediction.Contracts.Time.JstTime.Now();
        // DB outbox is the priority queue. Consider every due row so a recently-created
        // Realtime task cannot be hidden behind an older Background page.
        var remaining = (await store.GetPendingDispatchesAsync(now, int.MaxValue, cancellationToken).ConfigureAwait(false))
            .Where(x => x.Definition.Value == "race-odds" || _options.AggregationDelayMilliseconds <= 0
                || x.CreatedAt <= now.AddMilliseconds(-_options.AggregationDelayMilliseconds)).ToList();
        for (var sent = 0; sent < Math.Max(1, _options.DispatchBatchSize) && remaining.Count > 0; sent++)
        {
            var selected = _allocator.Select(remaining.Select(x => new FairCollectionCandidate(
                    x.Notification.TaskId, x.Lane, x.Priority, x.AvailableAt, x.CreatedAt)), now,
                await store.GetConsecutiveRealtimeDispatchCountAsync(cancellationToken).ConfigureAwait(false));
            if (selected is null) break;
            var item = remaining.Single(x => x.Notification.TaskId == selected.TaskId);
            var compatibility = CreateCompatibility(item);
            var maxTasks = GetMaxTasks(compatibility, item);
            var group = remaining.Where(x => IsCompatible(item, x))
                .OrderBy(x => RouteCourse(x)).ThenBy(x => RouteRaceNumber(x)).ThenBy(x => RouteType(x))
                .ThenByDescending(x => x.Priority).ThenBy(x => x.CreatedAt).ThenBy(x => x.Notification.TaskId)
                .Take(maxTasks).ToList();
            var envelopeId = Guid.NewGuid();
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
                if (!await store.TryReserveDispatchesWithinCapacityAsync(group.Select(x => x.OutboxId).ToArray(), reservationToken,
                        envelopeId, now, TimeSpan.FromSeconds(Math.Max(10, _options.OutboxReservationSeconds)),
                        Math.Max(1, _options.MaxInFlightEnvelopes),
                        cancellationToken).ConfigureAwait(false))
                    continue;
                var receipt = await queue.SendAsync(envelope, cancellationToken).ConfigureAwait(false);
                if (!await store.MarkDispatchedAsync(group.Select(x => x.OutboxId).ToArray(), reservationToken,
                        envelopeId, receipt.MessageId, HorseRacingPrediction.Contracts.Time.JstTime.Now(), cancellationToken).ConfigureAwait(false))
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
        if (item.EffectiveDate.HasValue && item.Resource.Type is ResourceType.RaceCard or ResourceType.RaceResult or ResourceType.Race)
            return new(item.Resource.Provider, item.Definition, item.EffectiveDate, item.Lane,
                CollectionDispatchGroupKind.RaceDay, item.EffectiveDate.Value.ToString("yyyy-MM-dd"));
        if (item.Resource.Type == ResourceType.Horse
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
        ResourceType.Race or ResourceType.RaceCard => 0,
        ResourceType.RaceResult => 1,
        _ => 2,
    };
}
