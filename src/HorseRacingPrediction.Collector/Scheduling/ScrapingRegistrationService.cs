using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class ScrapingRegistrationService : BackgroundService
{
    private static readonly string JraProviderType = "JRA";

    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");

    private readonly AgentProcessingOptions _options;
    private readonly IJraSessionFactory _sessionFactory;
    private readonly JraScheduleCollectionWorkflowFactory _scheduleWorkflowFactory;
    private readonly IProcessingStateStore _stateStore;
    private readonly CollectionExecutionTrigger _executionTrigger;
    private readonly ILogger<ScrapingRegistrationService> _logger;

    public ScrapingRegistrationService(
        IOptions<AgentProcessingOptions> options,
        IJraSessionFactory sessionFactory,
        JraScheduleCollectionWorkflowFactory scheduleWorkflowFactory,
        IProcessingStateStore stateStore,
        CollectionExecutionTrigger executionTrigger,
        ILogger<ScrapingRegistrationService> logger)
    {
        _options = options.Value;
        _sessionFactory = sessionFactory;
        _scheduleWorkflowFactory = scheduleWorkflowFactory;
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
        var queuedJobs = false;

        // NOTE(Task24): 成績収集の月次/日次バックフィル計画(ResultBackfillPlanningRequest /
        // ResultMonthDiscoveryRequest)は、旧URL列挙方式に依存しており、新Jra層への移行は
        // 依然として対象外（過去年数分の初回バックフィルは別タスク）。
        // 一方、当日〜直近数日分の確定成績収集（AgentJobType.RaceResultCollection）は、
        // 新Jra層(IJraScheduleCollectionWorkflow + JraRaceListPage)のみで実現できるため、
        // 出馬表収集ジョブ登録と同じ枠組みでここに登録する（下記参照）。

        if (_options.EnableScheduleCollection || _options.EnableRaceResultCollection)
        {
            _logger.LogInformation("[収集登録] 予定収集開始: ReferenceDate={Date} LookaheadDays={LookaheadDays}",
                today,
                _options.ScheduleLookaheadDays);

            var upcomingDates = new List<DateOnly>();
            var resultCandidateDates = new List<DateOnly>();
            string? scheduleError = null;

            try
            {
                await using var session = await _sessionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
                var scheduleWorkflow = _scheduleWorkflowFactory(session);

                if (_options.EnableScheduleCollection)
                {
                    for (var offset = 0; offset <= Math.Max(0, _options.ScheduleLookaheadDays); offset++)
                    {
                        var date = today.AddDays(offset);
                        var courses = await scheduleWorkflow.CollectAsync(date, cancellationToken).ConfigureAwait(false);
                        if (courses.Any(x => x != RaceCourse.Unknown))
                        {
                            upcomingDates.Add(date);
                        }
                    }
                }

                // 成績収集（AgentJobType.RaceResultCollection）は「開催日を過ぎたレース」の
                // 確定成績を取りに行くジョブで、出馬表収集とは対象日付レンジが異なる
                // （ResultLookbackDays分だけ過去に遡る）。以前はJRAカレンダーで開催有無を
                // 確認せず、範囲内の全暦日について機械的にジョブを登録していたため、
                // 開催のない平日（例: 金曜日）にも無駄なジョブ・SQS送出・Lambda起動が
                // 発生していた。出馬表収集と同様にカレンダーで開催有無を確認し、
                // 開催がある日付のみ登録する。
                if (_options.EnableRaceResultCollection)
                {
                    for (var offset = -Math.Max(0, _options.ResultLookbackDays); offset <= Math.Max(0, _options.ResultLookaheadDays); offset++)
                    {
                        var date = today.AddDays(offset);
                        var courses = await scheduleWorkflow.CollectAsync(date, cancellationToken).ConfigureAwait(false);
                        if (courses.Any(x => x != RaceCourse.Unknown))
                        {
                            resultCandidateDates.Add(date);
                        }
                        else
                        {
                            _logger.LogInformation(
                                "[収集登録] 開催のない日付のため成績収集ジョブをスキップします。Date={Date}",
                                date);
                        }
                    }
                }
            }
            catch (JraCollectionException ex)
            {
                scheduleError = ex.Message;
            }

            if (scheduleError is not null)
            {
                _logger.LogWarning("[収集登録] 予定収集失敗: {Error}", scheduleError);
            }
            else
            {
                _logger.LogInformation(
                    "[収集登録] 予定収集完了: Upcoming={Upcoming}",
                    upcomingDates.Count);

                if (_options.EnableRaceCardCollection)
                {
                    foreach (var date in upcomingDates.Distinct().OrderBy(x => x))
                    {
                        var payload = new RaceCardCollectionJobPayload(date, JraProviderType);
                        var key = AgentJobKeyFactory.BuildRaceCardCollectionKey(JraProviderType, date);
                        var priority = CalculateRaceCardPriority(date, today);
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

                foreach (var date in resultCandidateDates.Distinct().OrderBy(x => x))
                {
                    var payload = new RaceResultCollectionJobPayload(date, JraProviderType, AgentWorkMode.Idle);
                    var key = AgentJobKeyFactory.BuildRaceResultCollectionKey(JraProviderType, date);
                    var priority = CalculateRaceResultPriority(date, today);
                    await _stateStore.ScheduleJobAsync(
                        AgentJobType.RaceResultCollection,
                        key,
                        AgentJobPayloadSerializer.Serialize(payload),
                        now,
                        priority,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    queuedJobs = true;
                    _logger.LogInformation("[収集登録] 成績収集ジョブを登録しました。Date={Date}", date);
                }
            }
        }

        if (queuedJobs)
        {
            _executionTrigger.Signal();
        }
    }

    // JRAのレースはほぼ土曜・日曜に集中するため、直近の週末（今日から7日以内の
    // 土曜/日曜）は当日と同等の優先度で収集する。それ以外の未来日は従来通りの
    // 低優先度のまま据え置く。
    internal static int CalculateRaceCardPriority(DateOnly date, DateOnly today)
        => date == today || IsUpcomingWeekend(date, today) ? 200 : 180;

    internal static int CalculateRaceResultPriority(DateOnly date, DateOnly today)
        => date == today || IsUpcomingWeekend(date, today) ? 190 : 170;

    internal static bool IsUpcomingWeekend(DateOnly date, DateOnly today)
    {
        if (date < today)
        {
            return false;
        }

        if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            return false;
        }

        return date.DayNumber - today.DayNumber <= 7;
    }
}
