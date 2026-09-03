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
        // ResultMonthDiscoveryRequest)は、旧URL列挙方式に依存する CollectionExecutionService 側の
        // 処理が現時点で無効化されているため、ここでの登録は一旦見送る
        // （指示書Task23の申し送り事項を参照。登録しても処理されないジョブが積み上がるだけになるため）。
        // 成績収集自体（AgentJobType.RaceResultCollection、当日〜直近日の確定成績）は
        // ExecuteRaceCardJobsAsync と同様の仕組みで別途スケジュールする必要があるが、
        // 本タスクの範囲（開催日程確認→出馬表収集ジョブ登録）には含まれない。

        if (_options.EnableScheduleCollection)
        {
            _logger.LogInformation("[収集登録] 予定収集開始: ReferenceDate={Date} LookaheadDays={LookaheadDays}",
                today,
                _options.ScheduleLookaheadDays);

            var upcomingDates = new List<DateOnly>();
            string? scheduleError = null;

            try
            {
                await using var session = await _sessionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
                var scheduleWorkflow = _scheduleWorkflowFactory(session);

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
            }
        }

        if (queuedJobs)
        {
            _executionTrigger.Signal();
        }
    }
}
