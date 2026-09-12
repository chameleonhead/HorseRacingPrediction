using System.Text.Json;
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
        var result = await store.RunWatchdogAsync(DateTimeOffset.UtcNow,
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
                using var document = JsonDocument.Parse(message.Body);
                var envelope = JsonSerializer.Deserialize<CollectionDispatchEnvelope>(message.Body, JsonOptions);
                if (envelope?.IsSupported() != true)
                    throw new JsonException("DLQ message did not contain a supported dispatch envelope.");
                foreach (var task in envelope.Tasks)
                    reconciled += await store.ReconcileDeadLetterAsync(task.TaskId,
                        task.DispatchGeneration, DateTimeOffset.UtcNow,
                        $"Worker envelope {envelope.EnvelopeId} exhausted delivery and entered the dead-letter queue.",
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
                var resumed = await store.ResumeIncompleteBackfillBatchesAsync(DateTimeOffset.UtcNow, stoppingToken)
                    .ConfigureAwait(false);
                if (resumed > 0) logger.LogWarning("Resumed {Count} incomplete backfill batch expansions.", resumed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Incomplete backfill recovery failed."); }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
        }
    }
}
