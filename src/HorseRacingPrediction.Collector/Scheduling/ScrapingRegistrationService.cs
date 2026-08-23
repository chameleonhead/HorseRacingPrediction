using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class ScrapingRegistrationService : BackgroundService
{
    private static readonly string JraProviderType = "JRA";

    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");

    private readonly AgentProcessingOptions _options;
    private readonly JraRaceScheduleCollectionWorkflow _scheduleCollectionWorkflow;
    private readonly IProcessingStateStore _stateStore;
    private readonly CollectionExecutionTrigger _executionTrigger;
    private readonly ILogger<ScrapingRegistrationService> _logger;

    public ScrapingRegistrationService(
        IOptions<AgentProcessingOptions> options,
        JraRaceScheduleCollectionWorkflow scheduleCollectionWorkflow,
        IProcessingStateStore stateStore,
        CollectionExecutionTrigger executionTrigger,
        ILogger<ScrapingRegistrationService> logger)
    {
        _options = options.Value;
        _scheduleCollectionWorkflow = scheduleCollectionWorkflow;
        _stateStore = stateStore;
        _executionTrigger = executionTrigger;
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

    public async Task RunOneCycleAsync(CancellationToken cancellationToken)
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Jst);
        var today = DateOnly.FromDateTime(now.Date);
        JraRaceScheduleCollectionResult? schedule = null;
        var workMode = AgentWorkMode.Idle;
        var queuedJobs = false;

        if (_options.EnableScheduleCollection)
        {
            _logger.LogInformation("[収集登録] 予定収集開始: ReferenceDate={Date} LookaheadDays={LookaheadDays}",
                today,
                _options.ScheduleLookaheadDays);

            schedule = await _scheduleCollectionWorkflow
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

                workMode = AgentWorkModeResolver.Resolve(today, schedule, _options.PreRaceLeadDays);
                _logger.LogInformation("[収集登録] 実行モード: {Mode}", workMode);
            }
        }

        if (_options.EnableRaceCardCollection
            && schedule is not null
            && string.IsNullOrWhiteSpace(schedule.Error))
        {
            foreach (var date in schedule.UpcomingRaceDates.Distinct().OrderBy(x => x))
            {
                var payload = new RaceCardCollectionJobPayload(date, JraProviderType);
                var key = AgentJobKeyFactory.BuildRaceCardCollectionKey(JraProviderType, date);
                var priority = date == today ? 200 : 180;
                await _stateStore.ScheduleJobAsync(
                    AgentJobType.RaceCardCollection,
                    key,
                    AgentJobPayloadSerializer.Serialize(payload),
                    now,
                    priority,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                queuedJobs = true;
                _logger.LogInformation("[収集登録] 出馬表収集ジョブを登録しました。Date={Date}", date);
            }
        }

        if (_options.EnableRaceResultCollection)
        {
            var planningPayload = new ResultBackfillPlanningRequestPayload(
                JraProviderType,
                Math.Max(1, _options.InitialResultBackfillYears));
            await _stateStore.EnqueueJobAsync(
                AgentJobType.ResultBackfillPlanningRequest,
                AgentJobKeyFactory.BuildResultBackfillPlanningRequestKey(JraProviderType),
                AgentJobPayloadSerializer.Serialize(planningPayload),
                now,
                priority: 40,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            queuedJobs = true;

            var currentMonth = new DateOnly(today.Year, today.Month, 1);
            queuedJobs |= await ScheduleResultMonthDiscoveryAsync(currentMonth, now, cancellationToken).ConfigureAwait(false);

            var previousMonth = currentMonth.AddMonths(-1);
            queuedJobs |= await ScheduleResultMonthDiscoveryAsync(previousMonth, now, cancellationToken).ConfigureAwait(false);
        }

        if (queuedJobs)
        {
            _executionTrigger.Signal();
        }
    }

    private async Task<bool> ScheduleResultMonthDiscoveryAsync(
        DateOnly targetMonth,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var payload = new ResultMonthDiscoveryRequestPayload(
            JraProviderType,
            targetMonth.Year,
            targetMonth.Month,
            RevisitIncompleteDays: true);
        var key = AgentJobKeyFactory.BuildResultMonthDiscoveryRequestKey(JraProviderType, targetMonth.Year, targetMonth.Month);
        await _stateStore.ScheduleJobAsync(
            AgentJobType.ResultMonthDiscoveryRequest,
            key,
            AgentJobPayloadSerializer.Serialize(payload),
            now,
            priority: 160,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[収集登録] 月次成績探索ジョブを登録しました。Month={Month}", targetMonth.ToString("yyyy-MM"));
        return true;
    }

    internal static IReadOnlyList<string> BuildPredictionCandidateRaceIds(IEnumerable<string> savedRaceIds)
        => savedRaceIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    internal static IReadOnlyList<DateOnly> BuildResultCollectionDates(
        DateOnly today,
        JraRaceScheduleCollectionResult? schedule,
        AgentWorkMode workMode,
        AgentProcessingOptions options)
    {
        var lookbackDays = workMode switch
        {
            AgentWorkMode.Live when options.SuppressHistoricalBackfillDuringLive => Math.Max(0, options.LiveResultLookbackDays),
            AgentWorkMode.PreRace => Math.Max(0, options.PreRaceResultLookbackDays),
            _ => Math.Max(0, options.ResultLookbackDays)
        };

        var start = today.AddDays(-lookbackDays);
        var end = today.AddDays(Math.Max(0, options.ResultLookaheadDays));
        var dates = new List<DateOnly>();

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            dates.Add(date);
        }

        if (workMode == AgentWorkMode.Live && schedule is not null)
        {
            foreach (var raceDate in schedule.RaceDates.Where(x => x >= start && x <= end))
            {
                if (!dates.Contains(raceDate))
                {
                    dates.Add(raceDate);
                }
            }
        }

        return dates
            .OrderBy(x => x)
            .ToList();
    }
}
