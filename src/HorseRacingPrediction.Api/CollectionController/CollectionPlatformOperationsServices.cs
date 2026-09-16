using System.Text.Json;
using HorseRacingPrediction.Api.Notifications;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionPlatformWatchdogService(
    CollectionPlatformStore store,
    IOptions<CollectionJobWatchdogOptions> options,
    ILogger<CollectionPlatformWatchdogService> logger) : BackgroundService
{
    private readonly CollectionJobWatchdogOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Collection platform watchdog cycle failed."); }
            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes)), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task<CollectionWatchdogResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        if ((await store.GetPipelineStateAsync(cancellationToken).ConfigureAwait(false)).IsPaused)
            return new(0, 0, 0);
        var result = await store.RunWatchdogAsync(HorseRacingPrediction.Contracts.Time.JstTime.Now(),
            Math.Max(1, _options.MaxJobDispatchAttempts),
            TimeSpan.FromMinutes(Math.Max(1, _options.DispatchGraceMinutes)), cancellationToken).ConfigureAwait(false);
        if (result.ReclaimedLeases + result.RedispatchedTasks + result.DeadLetteredTasks > 0)
            logger.LogWarning("Collection watchdog recovered leases={Reclaimed}, redispatched={Redispatched}, dead-lettered={DeadLettered}.",
                result.ReclaimedLeases, result.RedispatchedTasks, result.DeadLetteredTasks);
        return result;
    }
}

public sealed class CollectionPlatformDeadLetterReconciler(
    CollectionPlatformStore store,
    ICollectionPlatformTaskQueue queue,
    IOptions<CollectionDeadLetterQueueReconcilerOptions> options,
    ILogger<CollectionPlatformDeadLetterReconciler> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CollectionDeadLetterQueueReconcilerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Collection platform DLQ reconciliation cycle failed."); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds)), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var messages = await queue.ReceiveDeadLetterMessagesAsync(
            Math.Clamp(_options.MaxMessagesPerCycle, 1, 10), cancellationToken).ConfigureAwait(false);
        var reconciled = 0;
        foreach (var message in messages)
        {
            try
            {
                var dispatch = ReadDeadLetterDispatch(message.Body);
                foreach (var task in dispatch.Tasks)
                    reconciled += await store.ReconcileDeadLetterAsync(task.TaskId,
                        task.DispatchGeneration, HorseRacingPrediction.Contracts.Time.JstTime.Now(),
                        $"{dispatch.Description} exhausted delivery and entered the dead-letter queue.",
                        cancellationToken).ConfigureAwait(false) ? 1 : 0;
                await queue.DeleteDeadLetterMessageAsync(message.ReceiptHandle, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Malformed collection platform DLQ message retained for operator inspection. ReceiptHandle={ReceiptHandle}",
                    message.ReceiptHandle);
            }
        }
        return reconciled;
    }

    private static DeadLetterDispatch ReadDeadLetterDispatch(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("DLQ message must be a JSON object.");

        var envelope = JsonSerializer.Deserialize<CollectionDispatchEnvelope>(body, JsonOptions);
        if (envelope?.IsSupported() == true)
            return new(envelope.Tasks, $"Worker envelope {envelope.EnvelopeId}");

        var propertyNames = document.RootElement.EnumerateObject()
            .Select(x => x.Name).ToList();
        var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "taskId", "dispatchGeneration", "contractVersion" };
        if (propertyNames.Count != expectedNames.Count
            || propertyNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != expectedNames.Count
            || propertyNames.Any(x => !expectedNames.Contains(x)))
            throw new JsonException("DLQ message did not contain a supported dispatch envelope or legacy task notification.");

        var notification = JsonSerializer.Deserialize<CollectionTaskNotification>(body, JsonOptions);
        if (notification?.IsSupported() != true)
            throw new JsonException("DLQ message contained an invalid legacy task notification.");
        return new([new(notification.TaskId, notification.DispatchGeneration)],
            $"Legacy worker notification for task {notification.TaskId}");
    }

    private sealed record DeadLetterDispatch(IReadOnlyList<CollectionDispatchTaskReference> Tasks,
        string Description);
}

public sealed class CollectionBackfillRecoveryService(
    CollectionPlatformStore store,
    ILogger<CollectionBackfillRecoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var resumed = await store.ResumeIncompleteBackfillBatchesAsync(HorseRacingPrediction.Contracts.Time.JstTime.Now(), stoppingToken)
                    .ConfigureAwait(false);
                if (resumed > 0) logger.LogWarning("Resumed {Count} incomplete backfill batch expansions.", resumed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Incomplete backfill recovery failed."); }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
        }
    }
}

public sealed class CollectionPipelineAlertDispatchService(
    CollectionPlatformStore store,
    ICollectionPipelineAlertPublisher publisher,
    ILogger<CollectionPipelineAlertDispatchService> logger) : BackgroundService
{
    private const string Prefix = "Unexpected collection failure notification ";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Collection pipeline alert dispatch failed; it will be retried."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        var pipeline = await store.GetPipelineStateAsync(cancellationToken).ConfigureAwait(false);
        if (!pipeline.IsPaused || pipeline.Reason is null || !pipeline.Reason.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var end = pipeline.Reason.IndexOf(':', Prefix.Length);
        if (end < 0 || !Guid.TryParse(pipeline.Reason[Prefix.Length..end], out var notificationId)) return false;
        var notification = await store.GetUnpublishedFailureNotificationAsync(notificationId, cancellationToken)
            .ConfigureAwait(false);
        if (notification is null) return false;
        var reason = $"Incident={notification.NotificationId:D}; Task={notification.TaskId:D}; "
            + $"Resource={notification.Resource.Type}/{notification.Resource.Provider}/{notification.Resource.Id}; "
            + $"Definition={notification.Definition}; TaskStatus={notification.Status}; "
            + $"Error={notification.ErrorCode ?? "Unknown"}; "
            + $"OccurredAt={notification.FailedAt:O}; {notification.ErrorMessage}";
        try
        {
            await publisher.PublishCollectionStoppedAsync(reason, 1, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await store.MarkFailureNotificationPublishFailedAsync(notification.NotificationId,
                HorseRacingPrediction.Contracts.Time.JstTime.Now(), ex.Message, cancellationToken).ConfigureAwait(false);
            throw;
        }
        await store.MarkFailureNotificationPublishedAsync(notification.NotificationId,
            HorseRacingPrediction.Contracts.Time.JstTime.Now(), cancellationToken).ConfigureAwait(false);
        return true;
    }
}
