namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public interface ICollectionDefinitionHandler
{
    CollectionDefinitionId DefinitionId { get; }
    ResourceType ResourceType { get; }
    Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken cancellationToken);
}

public sealed class CollectionDefinitionHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, ICollectionDefinitionHandler> _handlers;

    public CollectionDefinitionHandlerRegistry(IEnumerable<ICollectionDefinitionHandler> handlers)
    {
        var items = handlers.ToList();
        var duplicate = items.GroupBy(x => x.DefinitionId.Value, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new InvalidOperationException($"Duplicate collection handler: {duplicate.Key}");
        _handlers = items.ToDictionary(x => x.DefinitionId.Value, StringComparer.Ordinal);
    }

    public ICollectionDefinitionHandler Resolve(CollectionDefinitionId definition, ResourceType resourceType)
    {
        if (!_handlers.TryGetValue(definition.Value, out var handler))
            throw new InvalidOperationException($"No collection handler is registered for {definition}.");
        if (handler.ResourceType != resourceType)
            throw new InvalidOperationException($"Handler {definition} expects {handler.ResourceType}, not {resourceType}.");
        return handler;
    }
}

public sealed record FairCollectionCandidate(Guid TaskId, CollectionLane Lane, int Priority,
    DateTimeOffset AvailableAt, DateTimeOffset CreatedAt);

public sealed class CollectionLaneAllocator
{
    private readonly int _maxConsecutiveRealtime;
    private int _consecutiveRealtime;

    public CollectionLaneAllocator(int maxConsecutiveRealtime = 4)
    {
        if (maxConsecutiveRealtime < 1) throw new ArgumentOutOfRangeException(nameof(maxConsecutiveRealtime));
        _maxConsecutiveRealtime = maxConsecutiveRealtime;
    }

    public FairCollectionCandidate? Select(IEnumerable<FairCollectionCandidate> candidates, DateTimeOffset now)
    {
        var due = candidates.Where(x => x.AvailableAt <= now).ToList();
        if (due.Count == 0) return null;
        var hasBackground = due.Any(x => x.Lane == CollectionLane.Background);
        CollectionLane? forcedLane = _consecutiveRealtime >= _maxConsecutiveRealtime && hasBackground
            ? CollectionLane.Background : null;
        var selected = due.Where(x => forcedLane is null || x.Lane == forcedLane)
            .OrderBy(x => LaneRank(x.Lane))
            .ThenByDescending(x => EffectivePriority(x, now))
            .ThenBy(x => x.AvailableAt)
            .ThenBy(x => x.CreatedAt)
            .First();
        _consecutiveRealtime = selected.Lane == CollectionLane.Realtime ? _consecutiveRealtime + 1 : 0;
        return selected;
    }

    private static int LaneRank(CollectionLane lane) => lane switch
    {
        CollectionLane.Realtime => 0,
        CollectionLane.Normal => 1,
        CollectionLane.Background => 2,
        _ => 3,
    };

    private static int EffectivePriority(FairCollectionCandidate candidate, DateTimeOffset now)
    {
        var ageBoost = Math.Min(30, Math.Max(0, (int)(now - candidate.CreatedAt).TotalHours / 6));
        return candidate.Priority + ageBoost;
    }
}

public sealed class CollectionTaskExecutor(CollectionPlatformStore store, CollectionDefinitionHandlerRegistry handlers)
{
    public async Task<bool> ExecuteAsync(CollectionTaskNotification notification, DateTimeOffset now,
        TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var task = await store.AcquireAsync(notification.TaskId, notification.DispatchGeneration,
            now, leaseDuration, cancellationToken).ConfigureAwait(false);
        if (task is null) return false;
        CollectionAttemptCompletion result;
        try
        {
            var handler = handlers.Resolve(task.Definition, task.Resource.Type);
            result = await handler.CollectAsync(task, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = new(CollectionAttemptResult.TransientFailure, "Cancelled", "Collection was cancelled.",
                RetryAt: now.AddMinutes(1));
        }
        catch (Exception ex)
        {
            result = new(CollectionAttemptResult.PermanentFailure, ex.GetType().Name, ex.Message);
        }
        return await store.CompleteAttemptAsync(task.TaskId, task.LeaseToken, DateTimeOffset.UtcNow,
            result, cancellationToken).ConfigureAwait(false);
    }
}
