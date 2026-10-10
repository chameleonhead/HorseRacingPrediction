using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common.Time;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionPlanningScheduler
{
    private readonly CollectionPlatformStore _store;
    private readonly CollectionRuntimeStatusRecorder? _runtimeStatus;

    public CollectionPlanningScheduler(CollectionPlatformStore store)
        : this(store, null)
    {
    }

    internal CollectionPlanningScheduler(CollectionPlatformStore store,
        CollectionRuntimeStatusRecorder? runtimeStatus)
    {
        _store = store;
        _runtimeStatus = runtimeStatus;
    }

    internal async Task RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = _runtimeStatus?.BeginCycle(CollectionRuntimeAction.DiscoveryPlanner);
        var inspected = 0;
        var created = 0;
        try
        {
            if ((await _store.GetPipelineStateAsync(cancellationToken).ConfigureAwait(false)).IsPaused)
            {
                if (token is { } pausedToken)
                    _runtimeStatus!.CompleteCycle(pausedToken, CollectionRuntimeReason.Paused);
                return;
            }

            var jstNow = now.ToOffset(JstTime.Offset);
            var planningBucketHour = jstNow.Hour / 3 * 3;
            var bucket = new DateTimeOffset(jstNow.Year, jstNow.Month, jstNow.Day, planningBucketHour, 0, 0,
                JstTime.Offset);
            var resource = new ResourceKey(CollectionResourceType.Race, "JRA", $"discovery:{bucket:yyyyMMddHH}");
            var definition = new CollectionDefinitionId("race-discovery");
            // A planning bucket is a logical resource. Once registered, its state is the durable
            // evidence that this bucket was planned; terminal tasks must not be recreated every minute.
            var state = await _store.GetStateAsync(resource, definition, cancellationToken).ConfigureAwait(false);
            inspected++;
            if (state is null)
            {
                var receipt = await _store.RequestAsync(resource, definition, 1, CollectionReason.Discovery, jstNow,
                    CollectionLane.Realtime, (int)CollectionPriority.High,
                    effectiveDate: DateOnly.FromDateTime(jstNow.DateTime),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (receipt.CreatedTask)
                {
                    created++;
                    if (token is { } progressToken) _runtimeStatus!.RecordProgress(progressToken);
                }
                else if (receipt.DeferredByRepairHold)
                {
                    if (token is { } heldToken)
                        _runtimeStatus!.CompleteCycle(heldToken, CollectionRuntimeReason.RepairHeld,
                            new(inspected, created));
                    return;
                }
            }

            if (token is { } completedToken)
                _runtimeStatus!.CompleteCycle(completedToken,
                    created == 0 ? CollectionRuntimeReason.NoDueWork : null, new(inspected, created));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (token is { } cancelledToken)
                _runtimeStatus!.CancelCycle(cancelledToken, new(inspected, created));
            throw;
        }
        catch
        {
            if (token is { } failedToken)
                _runtimeStatus!.FailCycle(failedToken, new(inspected, created));
            throw;
        }
    }
}
