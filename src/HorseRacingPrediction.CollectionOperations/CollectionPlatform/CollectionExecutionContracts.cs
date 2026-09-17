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

public sealed record CollectionLaneDispatchState(int ConsecutiveRealtime, CollectionLane? LastNonRealtimeLane)
{
    public static CollectionLaneDispatchState Empty { get; } = new(0, null);

    public CollectionLaneDispatchState Advance(CollectionLane lane) => lane == CollectionLane.Realtime
        ? this with { ConsecutiveRealtime = ConsecutiveRealtime + 1 }
        : new(0, lane);
}

public sealed class CollectionLaneAllocator
{
    private readonly int _maxConsecutiveRealtime;

    public CollectionLaneAllocator(int maxConsecutiveRealtime = 4)
    {
        if (maxConsecutiveRealtime < 1) throw new ArgumentOutOfRangeException(nameof(maxConsecutiveRealtime));
        _maxConsecutiveRealtime = maxConsecutiveRealtime;
    }

    public FairCollectionCandidate? Select(IEnumerable<FairCollectionCandidate> candidates, DateTimeOffset now,
        CollectionLaneDispatchState? dispatchState = null)
    {
        var state = dispatchState ?? CollectionLaneDispatchState.Empty;
        var due = candidates.Where(x => x.AvailableAt <= now).ToList();
        if (due.Count == 0) return null;
        var selectedLane = SelectLane(due, state);
        return due.Where(x => x.Lane == selectedLane)
            .OrderByDescending(x => EffectivePriority(x, now))
            .ThenBy(x => x.AvailableAt)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.TaskId)
            .First();
    }

    private CollectionLane SelectLane(IReadOnlyCollection<FairCollectionCandidate> due,
        CollectionLaneDispatchState state)
    {
        var hasRealtime = due.Any(x => x.Lane == CollectionLane.Realtime);
        var hasNormal = due.Any(x => x.Lane == CollectionLane.Normal);
        var hasBackground = due.Any(x => x.Lane == CollectionLane.Background);
        if (hasRealtime && (state.ConsecutiveRealtime < _maxConsecutiveRealtime
                            || (!hasNormal && !hasBackground)))
            return CollectionLane.Realtime;
        if (hasNormal && hasBackground)
            return state.LastNonRealtimeLane == CollectionLane.Normal
                ? CollectionLane.Background
                : CollectionLane.Normal;
        if (hasNormal) return CollectionLane.Normal;
        if (hasBackground) return CollectionLane.Background;
        return CollectionLane.Realtime;
    }

    private static int EffectivePriority(FairCollectionCandidate candidate, DateTimeOffset now)
    {
        var ageBoost = Math.Min(30, Math.Max(0, (int)(now - candidate.CreatedAt).TotalHours / 6));
        return candidate.Priority + ageBoost;
    }
}

public static class CollectionAttemptFailureClassifier
{
    public static CollectionAttemptCompletion FromException(Exception exception)
    {
        var result = exception switch
        {
            HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } =>
                CollectionAttemptResult.ResourceNotFound,
            HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } =>
                CollectionAttemptResult.AccessLimited,
            HttpRequestException { StatusCode: >= System.Net.HttpStatusCode.InternalServerError } =>
                CollectionAttemptResult.TransientFailure,
            TimeoutException or TaskCanceledException => CollectionAttemptResult.TransientFailure,
            _ => CollectionAttemptResult.PermanentFailure,
        };
        return new(result, exception.GetType().Name, exception.Message,
            HttpStatusCode: (exception as HttpRequestException)?.StatusCode is { } status ? (int)status : null);
    }

    public static CollectionAttemptCompletion WithTaskContext(
        CollectionAttemptCompletion completion, LeasedCollectionTask task) =>
        completion.Result == CollectionAttemptResult.Succeeded || completion.PageIdentification is not null
            ? completion
            : completion with
            {
                PageIdentification =
                    $"Definition={task.Definition.Value}; Resource={task.Resource.Type}:{task.Resource.Provider}:{task.Resource.Id}",
            };
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
        var completionToken = cancellationToken;
        try
        {
            var handler = handlers.Resolve(task.Definition, task.Resource.Type);
            result = await handler.CollectAsync(task, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = new(CollectionAttemptResult.TransientFailure, "Cancelled", "Collection was cancelled.",
                RetryAt: now.AddMinutes(1));
            completionToken = CancellationToken.None;
        }
        catch (Exception ex)
        {
            result = CollectionAttemptFailureClassifier.FromException(ex);
        }
        result = CollectionAttemptFailureClassifier.WithTaskContext(result, task);
        return await store.CompleteAttemptAsync(task.TaskId, task.LeaseToken, HorseRacingPrediction.Contracts.Time.JstTime.Now(),
            result, completionToken).ConfigureAwait(false);
    }
}
