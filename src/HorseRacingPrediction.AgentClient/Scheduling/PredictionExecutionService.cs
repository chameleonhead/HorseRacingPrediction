using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class PredictionExecutionService : BackgroundService
{
    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");

    private readonly AgentProcessingOptions _options;
    private readonly ProcessingStateStore _stateStore;
    private readonly HistoricalDataRequestTracker _historicalDataRequestTracker;
    private readonly PredictionWorkflow _predictionWorkflow;
    private readonly ILogger<PredictionExecutionService> _logger;

    public PredictionExecutionService(
        IOptions<AgentProcessingOptions> options,
        ProcessingStateStore stateStore,
        HistoricalDataRequestTracker historicalDataRequestTracker,
        PredictionWorkflow predictionWorkflow,
        ILogger<PredictionExecutionService> logger)
    {
        _options = options.Value;
        _stateStore = stateStore;
        _historicalDataRequestTracker = historicalDataRequestTracker;
        _predictionWorkflow = predictionWorkflow;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("PredictionExecutionService は無効化されています。");
            return;
        }

        if (!_options.EnablePredictionExecution)
        {
            _logger.LogInformation("PredictionExecutionService は設定で無効化されています。データ収集のみ継続します。");
            return;
        }

        _logger.LogInformation("PredictionExecutionService を開始しました。");

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
                _logger.LogError(ex, "予想実行サイクルでエラーが発生しました。");
            }

            var delay = TimeSpan.FromMinutes(Math.Max(1, _options.PredictionIntervalMinutes));
            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOneCycleAsync(CancellationToken cancellationToken)
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Jst);
        var minAge = TimeSpan.FromMinutes(Math.Max(0, _options.PredictionMinAgeMinutes));

        var candidates = await _stateStore
            .TakeReadyPredictionCandidatesAsync(now, minAge, _options.PredictionBatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            _logger.LogDebug("[予想] 処理対象レースはありません。");
            return;
        }

        foreach (var raceId in candidates)
        {
            try
            {
                if (_options.BlockPredictionWhileHistoricalRequestsPending)
                {
                    var summary = await _historicalDataRequestTracker
                        .GetOutstandingRequestsAsync(raceId, cancellationToken)
                        .ConfigureAwait(false);
                    if (summary.TotalPendingRequests > 0)
                    {
                        _logger.LogInformation(
                            "[予想] 過去データ補完待ちのため再投入します。RaceId={RaceId} HorseRequests={HorseRequests} JockeyRequests={JockeyRequests} RaceResultRequests={RaceResultRequests}",
                            raceId,
                            summary.PendingHorseRequests,
                            summary.PendingJockeyRequests,
                            summary.PendingRaceResultRequests);

                        await _stateStore.RequeuePredictionCandidateAsync(
                            raceId,
                            now.AddMinutes(Math.Max(1, _options.HistoricalRequestRetryDelayMinutes)),
                            "Historical data requests are still pending.",
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                var result = await _predictionWorkflow.RunAsync(raceId, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "[予想] 完了: RaceId={RaceId} SummaryLength={SummaryLength}",
                    raceId,
                    result.PredictionSummary.Length);

                await _stateStore.MarkPredictionCompletedAsync(raceId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[予想] 失敗。再キューへ戻します。RaceId={RaceId}", raceId);
                await _stateStore
                    .RequeuePredictionCandidateAsync(raceId, now, ex.Message, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
