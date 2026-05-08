using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class ScrapingRegistrationService : BackgroundService
{
    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");

    private readonly AgentProcessingOptions _options;
    private readonly JraRaceScheduleCollectionWorkflow _scheduleCollectionWorkflow;
    private readonly JraRaceResultCollectionWorkflow _resultCollectionWorkflow;
    private readonly ProcessingStateStore _stateStore;
    private readonly RaceTextInsightCollector _insightCollector;
    private readonly ILogger<ScrapingRegistrationService> _logger;

    public ScrapingRegistrationService(
        IOptions<AgentProcessingOptions> options,
        JraRaceScheduleCollectionWorkflow scheduleCollectionWorkflow,
        JraRaceResultCollectionWorkflow resultCollectionWorkflow,
        ProcessingStateStore stateStore,
        RaceTextInsightCollector insightCollector,
        ILogger<ScrapingRegistrationService> logger)
    {
        _options = options.Value;
        _scheduleCollectionWorkflow = scheduleCollectionWorkflow;
        _resultCollectionWorkflow = resultCollectionWorkflow;
        _stateStore = stateStore;
        _insightCollector = insightCollector;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("ScrapingRegistrationService は無効化されています。");
            return;
        }

        _logger.LogInformation("ScrapingRegistrationService を開始しました。");

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
                _logger.LogError(ex, "スクレイピング登録サイクルでエラーが発生しました。");
            }

            var delay = TimeSpan.FromMinutes(Math.Max(1, _options.ScrapingIntervalMinutes));
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
        var today = DateOnly.FromDateTime(now.Date);

        if (_options.EnableScheduleCollection)
        {
            _logger.LogInformation("[収集登録] 予定収集開始: ReferenceDate={Date} LookaheadDays={LookaheadDays}",
                today,
                _options.ScheduleLookaheadDays);

            var schedule = await _scheduleCollectionWorkflow
                .CollectAsync(today, _options.ScheduleLookaheadDays, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(schedule.Error))
            {
                _logger.LogWarning("[収集登録] 予定収集失敗: {Error}", schedule.Error);
            }
            else
            {
                _logger.LogInformation(
                    "[収集登録] 予定収集完了: Collected={Collected} Upcoming={Upcoming}",
                    schedule.RaceDates.Count,
                    schedule.UpcomingRaceDates.Count);
            }
        }

        var start = today.AddDays(-Math.Max(0, _options.ResultLookbackDays));
        var end = today.AddDays(Math.Max(0, _options.ResultLookaheadDays));

        var allSavedRaceIds = new List<string>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            _logger.LogInformation("[収集登録] 成績収集開始: {Date}", date);
            var result = await _resultCollectionWorkflow.CollectAsync(date, cancellationToken).ConfigureAwait(false);

            allSavedRaceIds.AddRange(result.SavedRaceIds);

            _logger.LogInformation(
                "[収集登録] 成績収集完了: Date={Date} Saved={Saved} Errors={Errors}",
                date,
                result.SavedRaceIds.Count,
                result.Errors.Count);

            foreach (var error in result.Errors)
            {
                _logger.LogWarning("[収集登録] {Error}", error);
            }
        }

        var distinctRaceIds = allSavedRaceIds.Distinct(StringComparer.Ordinal).ToList();
        if (distinctRaceIds.Count == 0)
        {
            _logger.LogInformation("[収集登録] 保存済みレースIDはありませんでした。");
            return;
        }

        await _stateStore.EnqueuePredictionCandidatesAsync(distinctRaceIds, now, cancellationToken).ConfigureAwait(false);

        if (_options.EnableTextInsightCollection)
        {
            foreach (var raceId in distinctRaceIds)
            {
                await _insightCollector.CollectForRaceAsync(raceId, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
