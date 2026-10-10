using System.Text.Json;
using HorseRacingPrediction.Api.Notifications;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionPlatformWatchdogService : BackgroundService
{
    private readonly CollectionPlatformStore store;
    private readonly CollectionJobWatchdogOptions _options;
    private readonly ILogger<CollectionPlatformWatchdogService> logger;
    private readonly TimeProvider _timeProvider;
    private readonly CollectionRuntimeStatusRecorder? _runtimeStatus;

    public CollectionPlatformWatchdogService(CollectionPlatformStore store,
        IOptions<CollectionJobWatchdogOptions> options,
        ILogger<CollectionPlatformWatchdogService> logger)
        : this(store, options, logger, TimeProvider.System)
    {
    }

    public CollectionPlatformWatchdogService(CollectionPlatformStore store,
        IOptions<CollectionJobWatchdogOptions> options,
        ILogger<CollectionPlatformWatchdogService> logger,
        TimeProvider timeProvider)
        : this(store, options, logger, timeProvider, null)
    {
    }

    internal CollectionPlatformWatchdogService(CollectionPlatformStore store,
        IOptions<CollectionJobWatchdogOptions> options,
        ILogger<CollectionPlatformWatchdogService> logger,
        TimeProvider timeProvider,
        CollectionRuntimeStatusRecorder? runtimeStatus)
    {
        this.store = store;
        _options = options.Value;
        this.logger = logger;
        _timeProvider = timeProvider;
        _runtimeStatus = runtimeStatus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Collection platform watchdog cycle failed."); }
            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes)), _timeProvider, stoppingToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task<CollectionWatchdogResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var token = _runtimeStatus?.BeginCycle(CollectionRuntimeAction.TaskLeaseRecovery);
        try
        {
            var paused = (await store.GetPipelineStateAsync(cancellationToken).ConfigureAwait(false)).IsPaused;
            var result = paused
                ? new CollectionWatchdogResult(await store.ReclaimExpiredLeasesAsync(
                    CurrentTime(), cancellationToken).ConfigureAwait(false), 0, 0)
                : await store.RunWatchdogAsync(CurrentTime(),
                    Math.Max(1, _options.MaxJobDispatchAttempts),
                    TimeSpan.FromMinutes(Math.Max(1, _options.DispatchGraceMinutes)), cancellationToken)
                    .ConfigureAwait(false);

            if (result.ReclaimedLeases + result.RedispatchedTasks + result.DeadLetteredTasks > 0)
                logger.LogWarning("Collection watchdog recovered leases={Reclaimed}, redispatched={Redispatched}, dead-lettered={DeadLettered}.",
                    result.ReclaimedLeases, result.RedispatchedTasks, result.DeadLetteredTasks);
            if (token is { } cycleToken)
            {
                var changed = result.ReclaimedLeases + result.RedispatchedTasks + result.DeadLetteredTasks;
                if (changed > 0) _runtimeStatus!.RecordProgress(cycleToken);
                _runtimeStatus!.CompleteCycle(cycleToken, paused ? CollectionRuntimeReason.Paused
                    : changed == 0 ? CollectionRuntimeReason.NoDueWork : null,
                    new(Reclaimed: result.ReclaimedLeases, Sent: result.RedispatchedTasks,
                        Completed: result.DeadLetteredTasks));
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (token is { } cycleToken) _runtimeStatus!.CancelCycle(cycleToken);
            throw;
        }
        catch
        {
            if (token is { } cycleToken) _runtimeStatus!.FailCycle(cycleToken);
            throw;
        }
    }

    private DateTimeOffset CurrentTime() => _timeProvider.GetUtcNow()
        .ToOffset(HorseRacingPrediction.Contracts.Common.Time.JstTime.Offset);
}

public sealed class CollectionPlatformDeadLetterReconciler : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CollectionPlatformStore _store;
    private readonly ICollectionPlatformTaskQueue _queue;
    private readonly CollectionDeadLetterQueueReconcilerOptions _options;
    private readonly ILogger<CollectionPlatformDeadLetterReconciler> _logger;
    private readonly CollectionRuntimeStatusRecorder? _runtimeStatus;

    public CollectionPlatformDeadLetterReconciler(CollectionPlatformStore store,
        ICollectionPlatformTaskQueue queue, IOptions<CollectionDeadLetterQueueReconcilerOptions> options,
        ILogger<CollectionPlatformDeadLetterReconciler> logger)
        : this(store, queue, options, logger, null)
    {
    }

    internal CollectionPlatformDeadLetterReconciler(CollectionPlatformStore store,
        ICollectionPlatformTaskQueue queue, IOptions<CollectionDeadLetterQueueReconcilerOptions> options,
        ILogger<CollectionPlatformDeadLetterReconciler> logger, CollectionRuntimeStatusRecorder? runtimeStatus)
    {
        _store = store;
        _queue = queue;
        _options = options.Value;
        _logger = logger;
        _runtimeStatus = runtimeStatus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Collection platform DLQ reconciliation cycle failed."); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds)), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var token = _runtimeStatus?.BeginCycle(CollectionRuntimeAction.DeadLetterReconciliation);
        var inspected = 0;
        var audited = 0;
        var malformed = false;
        try
        {
            _ = _store; // Kept for source-compatible rolling deployments; DLQ audit is read-only.
            var messages = await _queue.ReceiveDeadLetterMessagesAsync(
                Math.Clamp(_options.MaxMessagesPerCycle, 1, 10), cancellationToken).ConfigureAwait(false);
            foreach (var message in messages)
            {
                inspected++;
                try
                {
                    var wake = JsonSerializer.Deserialize<CollectionWakeSignal>(message.Body, JsonOptions);
                    if (wake is not { ContractVersion: 1 } || wake.WakeId == Guid.Empty
                        || wake.DispatchEnvelopeId == Guid.Empty || string.IsNullOrWhiteSpace(wake.ReservationToken))
                        throw new JsonException("DLQ message did not contain a supported wake signal.");
                    _logger.LogError("Collection wake entered the dead-letter queue. WakeId={WakeId} EnvelopeId={EnvelopeId}",
                        wake.WakeId, wake.DispatchEnvelopeId);
                    await _queue.DeleteDeadLetterMessageAsync(message.ReceiptHandle, cancellationToken).ConfigureAwait(false);
                    audited++;
                    if (token is { } progressToken) _runtimeStatus!.RecordProgress(progressToken);
                }
                catch (JsonException ex)
                {
                    malformed = true;
                    _logger.LogError(ex, "Malformed collection platform DLQ message retained for operator inspection.");
                }
            }

            if (token is { } cycleToken)
            {
                var counts = new CollectionRuntimeCycleCounts(inspected, Completed: audited);
                if (malformed) _runtimeStatus!.FailCycle(cycleToken, counts);
                else _runtimeStatus!.CompleteCycle(cycleToken,
                    audited == 0 ? CollectionRuntimeReason.NoDueWork : null, counts);
            }
            return audited;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (token is { } cycleToken)
                _runtimeStatus!.CancelCycle(cycleToken, new(inspected, Completed: audited));
            throw;
        }
        catch
        {
            if (token is { } cycleToken)
                _runtimeStatus!.FailCycle(cycleToken, new(inspected, Completed: audited));
            throw;
        }
    }
}

public sealed class CollectionBackfillRecoveryService : BackgroundService
{
    private readonly CollectionPlatformStore store;
    private readonly ILogger<CollectionBackfillRecoveryService> logger;
    private readonly CollectionRuntimeStatusRecorder? runtimeStatus;
    private readonly TimeProvider timeProvider;

    public CollectionBackfillRecoveryService(CollectionPlatformStore store,
        ILogger<CollectionBackfillRecoveryService> logger)
        : this(store, logger, TimeProvider.System, null)
    {
    }

    internal CollectionBackfillRecoveryService(CollectionPlatformStore store,
        ILogger<CollectionBackfillRecoveryService> logger, TimeProvider timeProvider,
        CollectionRuntimeStatusRecorder? runtimeStatus)
    {
        this.store = store;
        this.logger = logger;
        this.timeProvider = timeProvider;
        this.runtimeStatus = runtimeStatus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Incomplete backfill recovery failed."); }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var token = runtimeStatus?.BeginCycle(CollectionRuntimeAction.BackfillRecovery);
        var committed = new CollectionPlatformStore.CollectionBatchCycleResult(0, 0, 0, 0, 0);
        var started = timeProvider.GetTimestamp();
        try
        {
            if ((await store.GetPipelineStateAsync(cancellationToken).ConfigureAwait(false)).IsPaused)
            {
                if (token is { } pausedToken)
                    runtimeStatus!.CompleteCycle(pausedToken, CollectionRuntimeReason.Paused);
                return;
            }

            var result = await store.RunBackfillRecoveryCycleAsync(
                timeProvider.GetUtcNow().ToOffset(HorseRacingPrediction.Contracts.Common.Time.JstTime.Offset),
                cancellationToken,
                cumulative =>
                {
                    committed = cumulative;
                    if (token is { } progressToken
                        && cumulative.TasksCreated + cumulative.TasksReused > 0)
                        runtimeStatus!.RecordProgress(progressToken);
                }, async token =>
                {
                    token.ThrowIfCancellationRequested();
                    if (timeProvider.GetElapsedTime(started) >= CollectionBackgroundRuntimeOptions.ProducerSliceBudget)
                        return CollectionBatchCycleStopReason.BudgetExhausted;
                    return (await store.GetPipelineStateAsync(token).ConfigureAwait(false)).IsPaused
                        ? CollectionBatchCycleStopReason.Paused
                        : CollectionBatchCycleStopReason.Continue;
                }).ConfigureAwait(false);
            committed = result;
            if (result.BatchesVisited > 0)
                logger.LogWarning("Visited {Count} incomplete backfill batch expansions.", result.BatchesVisited);
            if (token is { } completedToken)
                runtimeStatus!.CompleteCycle(completedToken,
                    result.StopReason switch
                    {
                        CollectionBatchCycleStopReason.Paused => CollectionRuntimeReason.Paused,
                        CollectionBatchCycleStopReason.BudgetExhausted => CollectionRuntimeReason.BudgetExhausted,
                        _ when result.BatchesVisited == 0 && result.LegacyRowsInspected == 0
                            && result.DatesVisited == 0 => CollectionRuntimeReason.NoDueWork,
                        _ => null,
                    },
                    ToRuntimeCounts(result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (token is { } cancelledToken) runtimeStatus!.CancelCycle(cancelledToken, ToRuntimeCounts(committed));
            throw;
        }
        catch
        {
            if (token is { } failedToken) runtimeStatus!.FailCycle(failedToken, ToRuntimeCounts(committed));
            throw;
        }
    }

    private static CollectionRuntimeCycleCounts ToRuntimeCounts(
        CollectionPlatformStore.CollectionBatchCycleResult result) =>
        new(result.LegacyRowsInspected + result.DatesVisited, result.TasksCreated,
            Completed: result.DatesVisited);
}

public sealed class CollectionPipelineAlertDispatchService : BackgroundService
{
    private const string Prefix = "Unexpected collection failure notification ";
    private readonly CollectionPlatformStore _store;
    private readonly ICollectionPipelineAlertPublisher _publisher;
    private readonly ILogger<CollectionPipelineAlertDispatchService> _logger;
    private readonly CollectionRuntimeStatusRecorder? _runtimeStatus;

    public CollectionPipelineAlertDispatchService(CollectionPlatformStore store,
        ICollectionPipelineAlertPublisher publisher, ILogger<CollectionPipelineAlertDispatchService> logger)
        : this(store, publisher, logger, null)
    {
    }

    internal CollectionPipelineAlertDispatchService(CollectionPlatformStore store,
        ICollectionPipelineAlertPublisher publisher, ILogger<CollectionPipelineAlertDispatchService> logger,
        CollectionRuntimeStatusRecorder? runtimeStatus)
    {
        _store = store;
        _publisher = publisher;
        _logger = logger;
        _runtimeStatus = runtimeStatus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Collection pipeline alert dispatch failed; it will be retried."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        var token = _runtimeStatus?.BeginCycle(CollectionRuntimeAction.Alerts);
        var inspected = 0;
        var sent = 0;
        try
        {
            var pipeline = await _store.GetPipelineStateAsync(cancellationToken).ConfigureAwait(false);
            if (!pipeline.IsPaused || pipeline.Reason is null || !pipeline.Reason.StartsWith(Prefix, StringComparison.Ordinal))
                return CompleteNoAlert(token, inspected);
            var end = pipeline.Reason.IndexOf(':', Prefix.Length);
            if (end < 0 || !Guid.TryParse(pipeline.Reason[Prefix.Length..end], out var notificationId))
                return CompleteNoAlert(token, inspected);
            var notification = await _store.GetUnpublishedFailureNotificationAsync(notificationId, cancellationToken)
                .ConfigureAwait(false);
            if (notification is null) return CompleteNoAlert(token, inspected);
            inspected++;
            var reason = $"Incident={notification.NotificationId:D}; Task={notification.TaskId:D}; "
                + $"Resource={notification.Resource.Type}/{notification.Resource.Provider}/{notification.Resource.Id}; "
                + $"Definition={notification.Definition}; TaskStatus={notification.Status}; "
                + $"Error={notification.ErrorCode ?? "Unknown"}; "
                + $"OccurredAt={notification.FailedAt:O}; {notification.ErrorMessage}";
            try
            {
                await _publisher.PublishCollectionStoppedAsync(reason, 1, cancellationToken).ConfigureAwait(false);
                sent++;
                if (token is { } progressToken) _runtimeStatus!.RecordProgress(progressToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                await _store.MarkFailureNotificationPublishFailedAsync(notification.NotificationId,
                    HorseRacingPrediction.Contracts.Common.Time.JstTime.Now(), ex.Message, cancellationToken)
                    .ConfigureAwait(false);
                throw;
            }
            await _store.MarkFailureNotificationPublishedAsync(notification.NotificationId,
                HorseRacingPrediction.Contracts.Common.Time.JstTime.Now(), cancellationToken).ConfigureAwait(false);
            if (token is { } completedToken)
                _runtimeStatus!.CompleteCycle(completedToken, null, new(inspected, Sent: 1, Completed: 1));
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (token is { } failedToken)
                _runtimeStatus!.FailCycle(failedToken, new(inspected, Sent: sent));
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (token is { } cancelledToken)
                _runtimeStatus!.CancelCycle(cancelledToken, new(inspected, Sent: sent));
            throw;
        }
    }

    private bool CompleteNoAlert(CollectionRuntimeCycleToken? token, int inspected)
    {
        if (token is { } cycleToken)
            _runtimeStatus!.CompleteCycle(cycleToken, CollectionRuntimeReason.NoDueWork, new(inspected));
        return false;
    }
}
