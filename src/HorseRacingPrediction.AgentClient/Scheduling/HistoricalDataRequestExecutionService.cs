using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class HistoricalDataRequestExecutionService : BackgroundService
{
    private readonly AgentProcessingOptions _options;
    private readonly ProcessingStateStore _stateStore;
    private readonly IHistoricalRaceResultCollector _historicalRaceResultCollector;
    private readonly IReadOnlyDictionary<string, IHistoricalDataRequestHandler> _handlers;
    private readonly ILogger<HistoricalDataRequestExecutionService> _logger;

    public HistoricalDataRequestExecutionService(
        IOptions<AgentProcessingOptions> options,
        ProcessingStateStore stateStore,
        IHistoricalRaceResultCollector historicalRaceResultCollector,
        IEnumerable<IHistoricalDataRequestHandler> handlers,
        ILogger<HistoricalDataRequestExecutionService> logger)
    {
        _options = options.Value;
        _stateStore = stateStore;
        _historicalRaceResultCollector = historicalRaceResultCollector;
        _handlers = handlers.ToDictionary(x => x.ProviderType, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("HistoricalDataRequestExecutionService は無効化されています。");
            return;
        }

        _logger.LogInformation("HistoricalDataRequestExecutionService を開始しました。");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOneCycleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "過去データ補完要求の実行サイクルでエラーが発生しました。");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _options.HistoricalRequestExecutionIntervalMinutes)), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOneCycleAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await ExecuteHistoricalRaceResultRequestsAsync(now, cancellationToken).ConfigureAwait(false);
        await ExecuteHorseHistoryRequestsAsync(now, cancellationToken).ConfigureAwait(false);
        await ExecuteJockeyHistoryRequestsAsync(now, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteHistoricalRaceResultRequestsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = await _stateStore.AcquireReadyJobsAsync(
            AgentJobType.HistoricalRaceResultCollectionRequest,
            now,
            TimeSpan.Zero,
            _options.HistoricalRequestBatchSize,
            TimeSpan.FromMinutes(Math.Max(1, _options.HistoricalRequestLeaseMinutes)),
            cancellationToken).ConfigureAwait(false);

        foreach (var job in jobs)
        {
            var payload = AgentJobPayloadSerializer.Deserialize<HistoricalRaceResultCollectionRequestPayload>(job.Payload);
            var result = await ExecuteDirectAsync(
                job.DeduplicationKey,
                AgentJobType.HistoricalRaceResultCollectionRequest,
                now,
                () => _historicalRaceResultCollector.CollectAsync(payload, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            LogResult(
                AgentJobType.HistoricalRaceResultCollectionRequest,
                payload.ProviderType,
                $"{payload.RaceDate:yyyy-MM-dd}|{payload.Racecourse}|{payload.RaceNumber:D2}",
                result);
        }
    }

    private async Task ExecuteHorseHistoryRequestsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = await _stateStore.AcquireReadyJobsAsync(
            AgentJobType.HorseHistoryCollectionRequest,
            now,
            TimeSpan.Zero,
            _options.HistoricalRequestBatchSize,
            TimeSpan.FromMinutes(Math.Max(1, _options.HistoricalRequestLeaseMinutes)),
            cancellationToken).ConfigureAwait(false);

        foreach (var job in jobs)
        {
            var payload = AgentJobPayloadSerializer.Deserialize<HorseHistoryCollectionRequestPayload>(job.Payload);
            var result = await ExecuteWithHandlerAsync(
                payload.ProviderType,
                job.DeduplicationKey,
                AgentJobType.HorseHistoryCollectionRequest,
                now,
                handler => handler.HandleHorseHistoryRequestAsync(payload, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            LogResult(AgentJobType.HorseHistoryCollectionRequest, payload.ProviderType, payload.HorseId, result);
        }
    }

    private async Task ExecuteJockeyHistoryRequestsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = await _stateStore.AcquireReadyJobsAsync(
            AgentJobType.JockeyHistoryCollectionRequest,
            now,
            TimeSpan.Zero,
            _options.HistoricalRequestBatchSize,
            TimeSpan.FromMinutes(Math.Max(1, _options.HistoricalRequestLeaseMinutes)),
            cancellationToken).ConfigureAwait(false);

        foreach (var job in jobs)
        {
            var payload = AgentJobPayloadSerializer.Deserialize<JockeyHistoryCollectionRequestPayload>(job.Payload);
            var result = await ExecuteWithHandlerAsync(
                payload.ProviderType,
                job.DeduplicationKey,
                AgentJobType.JockeyHistoryCollectionRequest,
                now,
                handler => handler.HandleJockeyHistoryRequestAsync(payload, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            LogResult(AgentJobType.JockeyHistoryCollectionRequest, payload.ProviderType, payload.JockeyId, result);
        }
    }

    private async Task<HistoricalDataRequestExecutionResult> ExecuteWithHandlerAsync(
        string providerType,
        string deduplicationKey,
        string jobType,
        DateTimeOffset now,
        Func<IHistoricalDataRequestHandler, Task<HistoricalDataRequestExecutionResult>> executeAsync,
        CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(providerType, out var handler))
        {
            var noHandlerMessage = $"No historical data request handler is registered for provider '{providerType}'.";
            await _stateStore.MarkJobAsDeadLetterAsync(jobType, deduplicationKey, noHandlerMessage, cancellationToken).ConfigureAwait(false);
            return HistoricalDataRequestExecutionResult.PermanentFailure(noHandlerMessage);
        }

        HistoricalDataRequestExecutionResult result;
        try
        {
            result = await executeAsync(handler).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = HistoricalDataRequestExecutionResult.Retry(ex.Message);
        }

        if (result.Succeeded)
        {
            await _stateStore.CompleteJobAsync(jobType, deduplicationKey, cancellationToken).ConfigureAwait(false);
            return result;
        }

        if (result.IsPermanentFailure)
        {
            await _stateStore.MarkJobAsDeadLetterAsync(jobType, deduplicationKey, result.Message, cancellationToken).ConfigureAwait(false);
            return result;
        }

        var attemptCount = await _stateStore.GetAttemptCountAsync(jobType, deduplicationKey, cancellationToken).ConfigureAwait(false);
        if (attemptCount >= Math.Max(1, _options.HistoricalRequestMaxAttempts))
        {
            await _stateStore.MarkJobAsDeadLetterAsync(jobType, deduplicationKey, result.Message, cancellationToken).ConfigureAwait(false);
            return HistoricalDataRequestExecutionResult.PermanentFailure(result.Message);
        }

        await _stateStore.RequeueJobAsync(
            jobType,
            deduplicationKey,
            now.AddMinutes(Math.Max(1, _options.HistoricalRequestRetryDelayMinutes)),
            result.Message,
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async Task<HistoricalDataRequestExecutionResult> ExecuteDirectAsync(
        string deduplicationKey,
        string jobType,
        DateTimeOffset now,
        Func<Task<HistoricalDataRequestExecutionResult>> executeAsync,
        CancellationToken cancellationToken)
    {
        HistoricalDataRequestExecutionResult result;
        try
        {
            result = await executeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = HistoricalDataRequestExecutionResult.Retry(ex.Message);
        }

        if (result.Succeeded)
        {
            await _stateStore.CompleteJobAsync(jobType, deduplicationKey, cancellationToken).ConfigureAwait(false);
            return result;
        }

        if (result.IsPermanentFailure)
        {
            await _stateStore.MarkJobAsDeadLetterAsync(jobType, deduplicationKey, result.Message, cancellationToken).ConfigureAwait(false);
            return result;
        }

        var attemptCount = await _stateStore.GetAttemptCountAsync(jobType, deduplicationKey, cancellationToken).ConfigureAwait(false);
        if (attemptCount >= Math.Max(1, _options.HistoricalRequestMaxAttempts))
        {
            await _stateStore.MarkJobAsDeadLetterAsync(jobType, deduplicationKey, result.Message, cancellationToken).ConfigureAwait(false);
            return HistoricalDataRequestExecutionResult.PermanentFailure(result.Message);
        }

        await _stateStore.RequeueJobAsync(
            jobType,
            deduplicationKey,
            now.AddMinutes(Math.Max(1, _options.HistoricalRequestRetryDelayMinutes)),
            result.Message,
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    private void LogResult(string jobType, string providerType, string subjectId, HistoricalDataRequestExecutionResult result)
    {
        if (result.Succeeded)
        {
            _logger.LogInformation("[過去データ補完] 完了: JobType={JobType} Provider={ProviderType} SubjectId={SubjectId}", jobType, providerType, subjectId);
            return;
        }

        if (result.IsPermanentFailure)
        {
            _logger.LogWarning("[過去データ補完] DeadLetter: JobType={JobType} Provider={ProviderType} SubjectId={SubjectId} Message={Message}", jobType, providerType, subjectId, result.Message);
            return;
        }

        _logger.LogWarning("[過去データ補完] 再試行予定: JobType={JobType} Provider={ProviderType} SubjectId={SubjectId} Message={Message}", jobType, providerType, subjectId, result.Message);
    }
}