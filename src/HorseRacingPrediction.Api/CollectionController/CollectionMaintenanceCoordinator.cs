using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionMaintenanceCoordinator : BackgroundService
{
    private readonly CollectionPlatformStore store;
    private readonly CollectionScheduleService scheduleService;
    private readonly CollectionPlanningScheduler planningScheduler;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<CollectionMaintenanceCoordinator> logger;
    private readonly CollectionRuntimeStatusRecorder? runtimeStatus;

    public CollectionMaintenanceCoordinator(CollectionPlatformStore store,
        CollectionScheduleService scheduleService, CollectionPlanningScheduler planningScheduler,
        TimeProvider timeProvider, ILogger<CollectionMaintenanceCoordinator> logger)
        : this(store, scheduleService, planningScheduler, timeProvider, logger, null)
    {
    }

    internal CollectionMaintenanceCoordinator(CollectionPlatformStore store,
        CollectionScheduleService scheduleService, CollectionPlanningScheduler planningScheduler,
        TimeProvider timeProvider, ILogger<CollectionMaintenanceCoordinator> logger,
        CollectionRuntimeStatusRecorder? runtimeStatus)
    {
        this.store = store;
        this.scheduleService = scheduleService;
        this.planningScheduler = planningScheduler;
        this.timeProvider = timeProvider;
        this.logger = logger;
        this.runtimeStatus = runtimeStatus;
    }

    private DateTimeOffset? _scheduleNextDue;
    private DateTimeOffset? _planningNextDue;
    private DateTimeOffset? _lastWallClock;
    private long _lastTimestamp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Collection producer maintenance cycle failed.");
            }

            var nextDue = _scheduleNextDue is null || _planningNextDue is null
                ? timeProvider.GetUtcNow()
                : (_scheduleNextDue.Value <= _planningNextDue.Value ? _scheduleNextDue.Value : _planningNextDue.Value);
            var current = timeProvider.GetUtcNow().ToOffset(nextDue.Offset);
            var delay = nextDue - current;
            if (delay <= TimeSpan.Zero) delay = TimeSpan.FromSeconds(1);
            if (delay > CollectionBackgroundRuntimeOptions.ProducerCadence)
                delay = CollectionBackgroundRuntimeOptions.ProducerCadence;
            await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().ToOffset(HorseRacingPrediction.Contracts.Common.Time.JstTime.Offset);
        var timestamp = timeProvider.GetTimestamp();
        if (_lastWallClock is { } previousWall)
        {
            var elapsed = timeProvider.GetElapsedTime(_lastTimestamp, timestamp);
            if (CollectionProducerCadencePolicy.IsClockJump(previousWall, now, elapsed,
                    CollectionBackgroundRuntimeOptions.ClockJumpTolerance))
            {
                scheduleService.ResetSweepCursor();
                _scheduleNextDue = now;
                _planningNextDue = now;
                logger.LogInformation("Collection producer cursors reset after a wall-clock adjustment.");
            }
        }
        _lastWallClock = now;
        _lastTimestamp = timestamp;
        _scheduleNextDue ??= now;
        _planningNextDue ??= now;

        CollectionPipelineState pipelineState;
        try
        {
            pipelineState = await store.GetPipelineStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            RecordPreflightFailure(CollectionRuntimeAction.RefreshPlanner);
            RecordPreflightFailure(CollectionRuntimeAction.DiscoveryPlanner);
            var failedAt = timeProvider.GetUtcNow().ToOffset(now.Offset);
            _scheduleNextDue = CollectionProducerCadencePolicy.NextDueAfterCompletion(
                failedAt, CollectionBackgroundRuntimeOptions.ProducerCadence);
            _planningNextDue = _scheduleNextDue;
            throw;
        }

        if (pipelineState.IsPaused)
        {
            RecordSkipped(CollectionRuntimeAction.RefreshPlanner, CollectionRuntimeReason.Paused);
            RecordSkipped(CollectionRuntimeAction.DiscoveryPlanner, CollectionRuntimeReason.Paused);
            var resumedAt = timeProvider.GetUtcNow().ToOffset(now.Offset);
            _scheduleNextDue = CollectionProducerCadencePolicy.NextDueAfterCompletion(
                resumedAt, CollectionBackgroundRuntimeOptions.ProducerCadence);
            _planningNextDue = _scheduleNextDue;
            return;
        }

        var sliceStarted = timeProvider.GetTimestamp();
        if (_scheduleNextDue.Value <= now)
        {
            CollectionRuntimeReason? scheduleStopReason = null;
            try
            {
                await scheduleService.RunOnceAsync(now, cancellationToken,
                    async token =>
                    {
                        scheduleStopReason = await GetProducerStopReasonAsync(sliceStarted, token)
                            .ConfigureAwait(false);
                        return scheduleStopReason is null;
                    },
                    () => scheduleStopReason).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Collection due-schedule producer failed; it will retry next cadence.");
            }
            finally
            {
                _scheduleNextDue = timeProvider.GetUtcNow().ToOffset(now.Offset)
                    + CollectionBackgroundRuntimeOptions.ProducerCadence;
            }
        }

        if (_planningNextDue.Value <= now && timeProvider.GetElapsedTime(sliceStarted) <
            CollectionBackgroundRuntimeOptions.ProducerSliceBudget)
        {
            try
            {
                var stopReason = await GetProducerStopReasonAsync(sliceStarted, cancellationToken)
                    .ConfigureAwait(false);
                if (stopReason is null)
                    await planningScheduler.RunOnceAsync(timeProvider.GetUtcNow().ToOffset(now.Offset), cancellationToken)
                        .ConfigureAwait(false);
                else
                    RecordSkipped(CollectionRuntimeAction.DiscoveryPlanner, stopReason.Value);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Collection discovery producer failed; it will retry next cadence.");
            }
            finally
            {
                _planningNextDue = timeProvider.GetUtcNow().ToOffset(now.Offset)
                    + CollectionBackgroundRuntimeOptions.ProducerCadence;
            }
        }
        else if (_planningNextDue.Value <= now)
        {
            RecordSkipped(CollectionRuntimeAction.DiscoveryPlanner, CollectionRuntimeReason.BudgetExhausted);
        }
    }

    private async Task<CollectionRuntimeReason?> GetProducerStopReasonAsync(long sliceStarted,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (timeProvider.GetElapsedTime(sliceStarted) >= CollectionBackgroundRuntimeOptions.ProducerSliceBudget)
            return CollectionRuntimeReason.BudgetExhausted;
        return (await store.GetPipelineStateAsync(cancellationToken).ConfigureAwait(false)).IsPaused
            ? CollectionRuntimeReason.Paused
            : null;
    }

    private void RecordSkipped(CollectionRuntimeAction action, CollectionRuntimeReason reason)
    {
        if (runtimeStatus?.BeginCycle(action) is { } token)
            runtimeStatus.CompleteCycle(token, reason);
    }

    private void RecordPreflightFailure(CollectionRuntimeAction action)
    {
        if (runtimeStatus?.BeginCycle(action) is { } token)
            runtimeStatus.FailCycle(token);
    }
}
