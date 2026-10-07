using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.CollectionController;

internal sealed record CollectionRuntimeActionConfiguration(
    CollectionRuntimeAction Action,
    bool Enabled,
    TimeSpan? EffectiveInterval);

internal sealed class CollectionRuntimeStatusRecorder
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly DateTimeOffset _instanceStartedAtUtc;
    private readonly Dictionary<CollectionRuntimeAction, CollectionRuntimeActionSnapshot> _actions;

    public CollectionRuntimeStatusRecorder(IEnumerable<CollectionRuntimeActionConfiguration> configurations,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _instanceStartedAtUtc = UtcNow();

        var entries = configurations.ToArray();
        if (entries.Length != Enum.GetValues<CollectionRuntimeAction>().Length
            || entries.Select(x => x.Action).Distinct().Count() != entries.Length
            || !Enum.GetValues<CollectionRuntimeAction>().All(action => entries.Any(x => x.Action == action)))
            throw new ArgumentException("Exactly one configuration is required for every runtime action.",
                nameof(configurations));
        if (entries.Any(x => x.EffectiveInterval is { } interval && interval <= TimeSpan.Zero))
            throw new ArgumentOutOfRangeException(nameof(configurations),
                "Effective intervals must be positive or null for event-driven actions.");

        _actions = entries.ToDictionary(x => x.Action, x => new CollectionRuntimeActionSnapshot(
            x.Action, x.Enabled, x.EffectiveInterval,
            x.Enabled ? CollectionRuntimeState.NotObserved : CollectionRuntimeState.Disabled,
            Reason: null, LastStartedAtUtc: null, LastCompletedAtUtc: null,
            LastSuccessfulCycleAtUtc: null, LastProgressAtUtc: null, LastDurationMilliseconds: null,
            InspectedCount: 0, CreatedCount: 0, ReclaimedCount: 0, SentCount: 0, CompletedCount: 0,
            ConsecutiveErrors: 0, CurrentCycleToken: 0, ActiveCycleStartTimestamp: null));
    }

    public CollectionRuntimeCycleToken? BeginCycle(CollectionRuntimeAction action)
    {
        lock (_sync)
        {
            var current = GetAction(action);
            if (!current.Enabled) return null;

            var token = current.CurrentCycleToken + 1;
            var next = CollectionRuntimeStatusTransitionPolicy.Begin(current, token, UtcNow(),
                _timeProvider.GetTimestamp());
            if (ReferenceEquals(current, next)) return null;
            _actions[action] = next;
            return new(action, token);
        }
    }

    public bool CompleteCycle(CollectionRuntimeCycleToken token, CollectionRuntimeReason? reason,
        CollectionRuntimeCycleCounts counts = default)
    {
        lock (_sync)
        {
            var current = GetAction(token.Action);
            var next = CollectionRuntimeStatusTransitionPolicy.Complete(current, token.Value, UtcNow(),
                GetDurationMilliseconds(current), reason, counts);
            return ReplaceIfChanged(token.Action, current, next);
        }
    }

    public bool CancelCycle(CollectionRuntimeCycleToken token, CollectionRuntimeCycleCounts counts = default)
    {
        lock (_sync)
        {
            var current = GetAction(token.Action);
            var next = CollectionRuntimeStatusTransitionPolicy.Cancel(current, token.Value, UtcNow(),
                GetDurationMilliseconds(current), counts);
            return ReplaceIfChanged(token.Action, current, next);
        }
    }

    public bool FailCycle(CollectionRuntimeCycleToken token, CollectionRuntimeCycleCounts counts = default)
    {
        lock (_sync)
        {
            var current = GetAction(token.Action);
            var next = CollectionRuntimeStatusTransitionPolicy.Fail(current, token.Value, UtcNow(),
                GetDurationMilliseconds(current), counts);
            return ReplaceIfChanged(token.Action, current, next);
        }
    }

    public bool RecordProgress(CollectionRuntimeCycleToken token)
    {
        lock (_sync)
        {
            var current = GetAction(token.Action);
            var next = CollectionRuntimeStatusTransitionPolicy.RecordProgress(current, token.Value, UtcNow());
            return ReplaceIfChanged(token.Action, current, next);
        }
    }

    public GetCollectionRuntimeStatusResponse GetSnapshot()
    {
        lock (_sync)
        {
            var actions = Enum.GetValues<CollectionRuntimeAction>()
                .Select(action => ToDto(_actions[action])).ToArray();
            return new(new(_instanceId, _instanceStartedAtUtc, UtcNow(), Array.AsReadOnly(actions)));
        }
    }

    private bool ReplaceIfChanged(CollectionRuntimeAction action, CollectionRuntimeActionSnapshot current,
        CollectionRuntimeActionSnapshot next)
    {
        if (ReferenceEquals(current, next)) return false;
        _actions[action] = next;
        return true;
    }

    private CollectionRuntimeActionSnapshot GetAction(CollectionRuntimeAction action) =>
        _actions.TryGetValue(action, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown runtime action.");

    private long GetDurationMilliseconds(CollectionRuntimeActionSnapshot current) =>
        current.ActiveCycleStartTimestamp is { } started
            ? Math.Max(0, (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds)
            : 0;

    private DateTimeOffset UtcNow() => _timeProvider.GetUtcNow().ToUniversalTime();

    private static CollectionRuntimeActionStatusDto ToDto(CollectionRuntimeActionSnapshot value) => new(
        value.Action, value.Enabled, value.EffectiveInterval, value.State, value.Reason,
        value.LastStartedAtUtc, value.LastCompletedAtUtc, value.LastSuccessfulCycleAtUtc,
        value.LastProgressAtUtc, value.LastDurationMilliseconds, value.InspectedCount, value.CreatedCount,
        value.ReclaimedCount, value.SentCount, value.CompletedCount, value.ConsecutiveErrors);
}
