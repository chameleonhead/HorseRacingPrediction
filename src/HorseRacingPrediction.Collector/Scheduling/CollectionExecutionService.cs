using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class CollectionExecutionService : BackgroundService
{
    private static readonly string JraProviderType = "JRA";

    // NOTE(Task23): 月次/日次成績探索・バックフィル計画(ResultMonthDiscoveryRequest/
    // ResultDayDiscoveryRequest/ResultDayCollectionRequest/ResultBackfillPlanningRequest)は、
    // 旧URL列挙方式(削除済みの JraRaceResultUrl / Scraping.Workflow 層)に強く依存しており、
    // 新 Jra/ 層への移行は本タスクの対象外（指示書Task23の範囲は RaceCard/RaceResult 収集のみ）。
    // 現状は無効のまま据え置く。将来的にはスケジュール収集(IJraScheduleCollectionWorkflow)と
    // レース一覧(IJraNavigator.ToRaceListAsync)を使い、URL列挙なしで同等の日次探索を組み立て直す
    // 必要がある。
    private static readonly string[] RecoverableJobTypes =
    [
        AgentJobType.RaceCardCollection,
        AgentJobType.RaceResultCollection
    ];

    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");

    private readonly AgentProcessingOptions _options;
    private readonly IProcessingStateStore _stateStore;
    private readonly IJraSessionFactory _sessionFactory;
    private readonly IDataCollectionWriteService _writeService;
    private readonly HistoricalDataRequestPlanner _historicalDataRequestPlanner;
    private readonly CollectionExecutionTrigger _executionTrigger;
    private readonly ILogger<CollectionExecutionService> _logger;

    public CollectionExecutionService(
        IOptions<AgentProcessingOptions> options,
        IProcessingStateStore stateStore,
        IJraSessionFactory sessionFactory,
        IDataCollectionWriteService writeService,
        HistoricalDataRequestPlanner historicalDataRequestPlanner,
        CollectionExecutionTrigger executionTrigger,
        ILogger<CollectionExecutionService> logger)
    {
        _options = options.Value;
        _stateStore = stateStore;
        _sessionFactory = sessionFactory;
        _writeService = writeService;
        _historicalDataRequestPlanner = historicalDataRequestPlanner;
        _executionTrigger = executionTrigger;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("CollectionExecutionService は無効化されています。");
            return;
        }

        _logger.LogInformation("CollectionExecutionService を開始しました。");

        var recoveredJobs = await _stateStore
            .RequeueRunningJobsAsync(RecoverableJobTypes, DateTimeOffset.UtcNow, stoppingToken)
            .ConfigureAwait(false);
        if (recoveredJobs > 0)
        {
            _logger.LogWarning(
                "CollectionExecutionService 起動時に孤立した収集ジョブを再キューしました。Count={Count}",
                recoveredJobs);
        }

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
                _logger.LogError(ex, "収集実行サイクルでエラーが発生しました。");
            }

            try
            {
                var delay = TimeSpan.FromMinutes(Math.Max(1, _options.CollectionExecutionIntervalMinutes));
                var signaled = await _executionTrigger.WaitAsync(delay, stoppingToken).ConfigureAwait(false);
                if (signaled)
                {
                    _logger.LogInformation("収集実行の即時トリガーを受信しました。次サイクルを開始します。");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task RunOneCycleAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await ExecuteRaceCardJobsAsync(now, cancellationToken).ConfigureAwait(false);
        await ExecuteRaceResultJobsAsync(now, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteRaceCardJobsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = await _stateStore
            .AcquireReadyJobsAsync(
                AgentJobType.RaceCardCollection,
                now,
                TimeSpan.Zero,
                _options.CollectionBatchSize,
                TimeSpan.FromMinutes(Math.Max(1, _options.CollectionLeaseMinutes)),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var job in jobs)
        {
            try
            {
                var payload = AgentJobPayloadSerializer.Deserialize<RaceCardCollectionJobPayload>(job.Payload);
                if (!string.Equals(payload.ProviderType, JraProviderType, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"未対応の ProviderType です: {payload.ProviderType}");
                }

                var (results, savedRaceIds) = await CollectRaceCardsAsync(payload.RaceDate, cancellationToken).ConfigureAwait(false);

                if (ShouldRetryRaceCardCollection(payload.RaceDate, results, now))
                {
                    _logger.LogInformation(
                        "[収集実行] 出馬表未公開のため再試行します: Date={Date}",
                        payload.RaceDate);

                    await _stateStore.RequeueJobAsync(
                        AgentJobType.RaceCardCollection,
                        job.DeduplicationKey,
                        now,
                        "Race card publication is not available yet. Retry scheduled.",
                        cancellationToken,
                        availableAt: now.AddMinutes(30)).ConfigureAwait(false);

                    continue;
                }

                await RecordRaceCardStatusesAsync(payload.RaceDate, results, now, cancellationToken).ConfigureAwait(false);

                var errorCount = results.Sum(x => x.Errors.Count);
                _logger.LogInformation(
                    "[収集実行] 出馬表収集完了: Date={Date} Saved={Saved} Errors={Errors}",
                    payload.RaceDate,
                    savedRaceIds.Count,
                    errorCount);

                foreach (var result in results)
                {
                    foreach (var error in result.Errors)
                    {
                        _logger.LogWarning("[収集実行] Course={Course} {Error}", result.Course, error);
                    }
                }

                var distinctRaceIds = savedRaceIds
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (distinctRaceIds.Count > 0)
                {
                    foreach (var raceId in distinctRaceIds)
                    {
                        var plan = await _historicalDataRequestPlanner
                            .EnsureRequestsForRaceAsync(raceId, now, cancellationToken)
                            .ConfigureAwait(false);

                        if (plan.RequestedHorseHistoryCount > 0
                            || plan.RequestedJockeyHistoryCount > 0
                            || plan.RequestedTrainerProfileCount > 0)
                        {
                            _logger.LogInformation(
                                "[収集実行] 馬・騎手・調教師情報取得要求を登録しました。RaceId={RaceId} HorseRequests={HorseRequests} JockeyRequests={JockeyRequests} TrainerRequests={TrainerRequests}",
                                raceId,
                                plan.RequestedHorseHistoryCount,
                                plan.RequestedJockeyHistoryCount,
                                plan.RequestedTrainerProfileCount);
                        }

                        if (plan.RequestedRaceResultCount > 0)
                        {
                            _logger.LogInformation(
                            "[収集実行] 過去レース結果取得要求を登録しました。RaceId={RaceId} RaceResultRequests={RaceResultRequests}",
                                raceId,
                            plan.RequestedRaceResultCount);
                        }
                    }

                    await _stateStore.EnqueuePredictionCandidatesAsync(distinctRaceIds, now, cancellationToken).ConfigureAwait(false);
                }

                await _stateStore.CompleteJobAsync(AgentJobType.RaceCardCollection, job.DeduplicationKey, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[収集実行] 出馬表収集ジョブ失敗。JobKey={JobKey}", job.DeduplicationKey);
                await _stateStore.FailJobAsync(
                    AgentJobType.RaceCardCollection,
                    job.DeduplicationKey,
                    ex.Message,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteRaceResultJobsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = await _stateStore
            .AcquireReadyJobsAsync(
                AgentJobType.RaceResultCollection,
                now,
                TimeSpan.Zero,
                _options.CollectionBatchSize,
                TimeSpan.FromMinutes(Math.Max(1, _options.CollectionLeaseMinutes)),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var job in jobs)
        {
            try
            {
                var payload = AgentJobPayloadSerializer.Deserialize<RaceResultCollectionJobPayload>(job.Payload);
                if (!string.Equals(payload.ProviderType, JraProviderType, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"未対応の ProviderType です: {payload.ProviderType}");
                }

                var (results, savedRaceIds) = await CollectRaceResultsAsync(payload.RaceDate, cancellationToken).ConfigureAwait(false);
                await RecordRaceResultStatusesAsync(payload.RaceDate, results, now, cancellationToken).ConfigureAwait(false);

                var errorCount = results.Sum(x => x.Errors.Count);
                _logger.LogInformation(
                    "[収集実行] 成績収集完了: Date={Date} Saved={Saved} Errors={Errors}",
                    payload.RaceDate,
                    savedRaceIds.Count,
                    errorCount);

                foreach (var result in results)
                {
                    foreach (var error in result.Errors)
                    {
                        _logger.LogWarning("[収集実行] RaceId={RaceId} {Error}", result.RaceId, error);
                    }
                }

                await _stateStore.CompleteJobAsync(AgentJobType.RaceResultCollection, job.DeduplicationKey, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[収集実行] 成績収集ジョブ失敗。JobKey={JobKey}", job.DeduplicationKey);
                await _stateStore.FailJobAsync(
                    AgentJobType.RaceResultCollection,
                    job.DeduplicationKey,
                    ex.Message,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 指定日に開催される全競馬場の出馬表を収集する。
    /// 開催競馬場の特定は <see cref="IJraScheduleCollectionWorkflow"/>、各競馬場の出馬表収集は
    /// <see cref="IJraRaceCardCollectionWorkflow"/> に委譲する（オーケストレーションのみ）。
    /// </summary>
    private async Task<(IReadOnlyList<RaceCardCollectionResult> Results, IReadOnlyList<string> SavedRaceIds)> CollectRaceCardsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken)
    {
        await using var session = await _sessionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

        var scheduleWorkflow = new JraScheduleCollectionWorkflow(session);
        var courses = await scheduleWorkflow.CollectAsync(raceDate, cancellationToken).ConfigureAwait(false);

        var cardWorkflow = new JraRaceCardCollectionWorkflow(session, _writeService);
        var results = new List<RaceCardCollectionResult>();
        var savedRaceIds = new List<string>();

        foreach (var course in courses.Where(x => x != RaceCourse.Unknown))
        {
            var result = await cardWorkflow.CollectAsync(raceDate, course, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            savedRaceIds.AddRange(result.RaceIds);
        }

        return (results, savedRaceIds);
    }

    /// <summary>
    /// 指定日に開催される全競馬場の確定成績を収集する。
    /// 開催競馬場・レース一覧の特定は <see cref="IJraScheduleCollectionWorkflow"/> と
    /// <see cref="JraSession.Navigate"/>（レース一覧ページ）、各レースの成績収集は
    /// <see cref="IJraRaceResultCollectionWorkflow"/> に委譲する（オーケストレーションのみ）。
    /// レース一覧ページ自体は出馬表収集と同じもの（<see cref="Scraping.Jra.Pages.JraRaceListPage"/>）を
    /// 利用するため、旧実装にあった専用のURL探索（削除済みの JraRaceResultUrl）は不要になった。
    /// </summary>
    private async Task<(IReadOnlyList<RaceResultCollectionResult> Results, IReadOnlyList<string> SavedRaceIds)> CollectRaceResultsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken)
    {
        await using var session = await _sessionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

        var scheduleWorkflow = new JraScheduleCollectionWorkflow(session);
        var courses = await scheduleWorkflow.CollectAsync(raceDate, cancellationToken).ConfigureAwait(false);

        var resultWorkflow = new JraRaceResultCollectionWorkflow(session, _writeService);
        var results = new List<RaceResultCollectionResult>();
        var savedRaceIds = new List<string>();

        foreach (var course in courses.Where(x => x != RaceCourse.Unknown))
        {
            var listPage = await session.Navigate.ToRaceListAsync(raceDate, course, cancellationToken).ConfigureAwait(false);
            if (listPage is not Scraping.Jra.Pages.JraRaceListPage raceList)
            {
                _logger.LogWarning(
                    "[収集実行] レース一覧ページを取得できませんでした（成績収集をスキップ）。Date={Date} Course={Course} Kind={Kind}",
                    raceDate, course, listPage.Kind);
                continue;
            }

            foreach (var race in raceList.Races)
            {
                cancellationToken.ThrowIfCancellationRequested();

                RaceResultCollectionResult result;
                try
                {
                    result = await resultWorkflow
                        .CollectAsync(new RaceId(raceDate, course, race.Number), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (JraCollectionException ex)
                {
                    // 成績未確定（レースがまだ終わっていない等）でページが取得できない場合はスキップする。
                    // Workflow自体はリトライを行わないため、リトライは既存のジョブ再実行サイクルに委ねる。
                    _logger.LogInformation(
                        ex,
                        "[収集実行] 成績ページ未取得のためスキップします。Date={Date} Course={Course} RaceNumber={RaceNumber}",
                        raceDate, course, race.Number);
                    continue;
                }

                results.Add(result);
                savedRaceIds.AddRange(result.SavedHorseNumbers.Count > 0 ? new[] { result.DataCollectionRaceId } : Array.Empty<string>());
            }
        }

        return (results, savedRaceIds.Distinct(StringComparer.Ordinal).ToList());
    }

    public Task RunTaskAsync(string jobType, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return jobType switch
        {
            AgentJobType.RaceCardCollection => ExecuteRaceCardJobsAsync(now, cancellationToken),
            AgentJobType.RaceResultCollection => ExecuteRaceResultJobsAsync(now, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported collection job type: {jobType}")
        };
    }

    private static bool ShouldRetryRaceCardCollection(
        DateOnly raceDate,
        IReadOnlyList<RaceCardCollectionResult> results,
        DateTimeOffset now)
    {
        if (results.Count > 0 && results.Any(x => x.RaceIds.Count > 0 || x.Errors.Count > 0))
        {
            return false;
        }

        var todayJst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Jst).Date);
        return raceDate >= todayJst;
    }

    private async Task RecordRaceCardStatusesAsync(
        DateOnly raceDate,
        IReadOnlyList<RaceCardCollectionResult> results,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var result in results)
        {
            var racecourse = RaceCourseNames.GetJraName(result.Course);

            foreach (var race in result.Races)
            {
                RaceDataCollectionState status;
                RaceDataCollectionErrorCode? errorCode = null;
                string? errorReason = null;

                if (race.RaceId is not null)
                {
                    status = RaceDataCollectionState.Succeeded;
                }
                else
                {
                    status = RaceDataCollectionState.Failed;
                    var classified = RaceDataCollectionErrorClassifier.Classify(
                        race.Error ?? "No card data was extracted for the discovered race.");
                    errorCode = classified.Code;
                    errorReason = classified.Reason;
                }

                await _stateStore.UpsertRaceCardCollectionStatusAsync(
                    raceDate,
                    racecourse,
                    race.RaceNumber,
                    race.RaceId,
                    race.RaceName,
                    race.SourceUrl,
                    status,
                    errorCode,
                    errorReason,
                    now,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RecordRaceResultStatusesAsync(
        DateOnly raceDate,
        IReadOnlyList<RaceResultCollectionResult> results,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var result in results)
        {
            var racecourse = RaceCourseNames.GetJraName(result.RaceId.Course);

            RaceDataCollectionState status;
            RaceDataCollectionErrorCode? errorCode = null;
            string? errorReason = null;

            if (result.SavedHorseNumbers.Count > 0)
            {
                status = RaceDataCollectionState.Succeeded;
            }
            else
            {
                status = RaceDataCollectionState.Failed;
                var explicitError = result.Errors.FirstOrDefault();
                var classified = RaceDataCollectionErrorClassifier.Classify(
                    explicitError ?? "No result data was extracted for the discovered race.");
                errorCode = classified.Code;
                errorReason = explicitError ?? classified.Reason;
            }

            await _stateStore.UpsertRaceResultCollectionStatusAsync(
                raceDate,
                racecourse,
                result.RaceId.Number,
                status == RaceDataCollectionState.Succeeded ? result.DataCollectionRaceId : null,
                raceName: null,
                sourceUrl: null,
                status,
                RaceResultAcquisitionOrigin.Scheduled,
                requestedByRaceId: null,
                errorCode,
                errorReason,
                now,
                cancellationToken).ConfigureAwait(false);
        }
    }
}
