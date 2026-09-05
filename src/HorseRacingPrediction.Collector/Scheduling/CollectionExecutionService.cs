using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Scheduling;

// NOTE(ジョブ粒度の検討): RaceCardCollection/RaceResultCollection はいずれも
// 「1開催日=1ジョブ」（CollectRaceCardsAsync/CollectRaceResultsAsync が当日開催の
// 全競馬場・全レースをジョブ内でループする）であり、レース単位のジョブ分割は行っていない。
// docs/lambda-collector-architecture.md にある「9分以内に終わらない場合はページ/レース単位に
// 分割する」という記述は将来の対処方針であり、現在有効な制約ではない
// （Program.cs の通り、Lambda呼び出し用の --once 実行経路自体が現在無効化されており、
// 常駐Hostモードのみが動いているため15分のLambdaタイムアウトを受けていない）。
// JraNavigator最短経路化後の実測は5レースで5分41秒（約68秒/レース）。JRAは1開催日あたり
// 最大30レース超（複数場開催時）になり得るため、単純外挿では約34分となり、将来Lambdaでの
// --once運用を復活させる場合は9分のLambda内部deadlineに収まらない可能性が高い。
// その場合はレース単位まで分割する必要はなく、既にワークフロー呼び出しが競馬場単位で
// 独立している（CollectRaceCardsAsync/CollectRaceResultsAsync 内の course ループ）ため、
// 競馬場単位でジョブを分割するのが最小変更で済む対処となる。現時点ではLambda運用自体が
// 無効化されており必要性がないため、本タスクでは分割の実装は行わない。
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
    private readonly JraScheduleCollectionWorkflowFactory _scheduleWorkflowFactory;
    private readonly JraRaceCardCollectionWorkflowFactory _raceCardWorkflowFactory;
    private readonly JraRaceResultCollectionWorkflowFactory _raceResultWorkflowFactory;
    private readonly HistoricalDataRequestPlanner _historicalDataRequestPlanner;
    private readonly CollectionExecutionTrigger _executionTrigger;
    private readonly ILogger<CollectionExecutionService> _logger;

    public CollectionExecutionService(
        IOptions<AgentProcessingOptions> options,
        IProcessingStateStore stateStore,
        IJraSessionFactory sessionFactory,
        JraScheduleCollectionWorkflowFactory scheduleWorkflowFactory,
        JraRaceCardCollectionWorkflowFactory raceCardWorkflowFactory,
        JraRaceResultCollectionWorkflowFactory raceResultWorkflowFactory,
        HistoricalDataRequestPlanner historicalDataRequestPlanner,
        CollectionExecutionTrigger executionTrigger,
        ILogger<CollectionExecutionService> logger)
    {
        _options = options.Value;
        _stateStore = stateStore;
        _sessionFactory = sessionFactory;
        _scheduleWorkflowFactory = scheduleWorkflowFactory;
        _raceCardWorkflowFactory = raceCardWorkflowFactory;
        _raceResultWorkflowFactory = raceResultWorkflowFactory;
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
            using var jobTimeoutCts = CreateJobTimeoutCts(cancellationToken);

            try
            {
                var payload = AgentJobPayloadSerializer.Deserialize<RaceCardCollectionJobPayload>(job.Payload);
                if (!string.Equals(payload.ProviderType, JraProviderType, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"未対応の ProviderType です: {payload.ProviderType}");
                }

                var (results, savedRaceIds) = await CollectRaceCardsAsync(payload.RaceDate, jobTimeoutCts.Token).ConfigureAwait(false);

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
                if (IsJobTimeout(ex, jobTimeoutCts, cancellationToken))
                {
                    _logger.LogWarning(
                        "[収集実行] 出馬表収集ジョブがタイムアウトしました（{TimeoutMinutes}分、ハング検知の安全策）。JobKey={JobKey}",
                        _options.CollectionJobTimeoutMinutes,
                        job.DeduplicationKey);
                }
                else
                {
                    _logger.LogWarning(ex, "[収集実行] 出馬表収集ジョブ失敗。JobKey={JobKey}", job.DeduplicationKey);
                }

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
            using var jobTimeoutCts = CreateJobTimeoutCts(cancellationToken);

            try
            {
                var payload = AgentJobPayloadSerializer.Deserialize<RaceResultCollectionJobPayload>(job.Payload);
                if (!string.Equals(payload.ProviderType, JraProviderType, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"未対応の ProviderType です: {payload.ProviderType}");
                }

                var (results, savedRaceIds) = await CollectRaceResultsAsync(payload.RaceDate, jobTimeoutCts.Token).ConfigureAwait(false);
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
                if (IsJobTimeout(ex, jobTimeoutCts, cancellationToken))
                {
                    _logger.LogWarning(
                        "[収集実行] 成績収集ジョブがタイムアウトしました（{TimeoutMinutes}分、ハング検知の安全策）。JobKey={JobKey}",
                        _options.CollectionJobTimeoutMinutes,
                        job.DeduplicationKey);
                }
                else
                {
                    _logger.LogWarning(ex, "[収集実行] 成績収集ジョブ失敗。JobKey={JobKey}", job.DeduplicationKey);
                }

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

        var scheduleWorkflow = _scheduleWorkflowFactory(session);
        var courses = await scheduleWorkflow.CollectAsync(raceDate, cancellationToken).ConfigureAwait(false);

        var cardWorkflow = _raceCardWorkflowFactory(session);
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

        var scheduleWorkflow = _scheduleWorkflowFactory(session);
        var courses = await scheduleWorkflow.CollectAsync(raceDate, cancellationToken).ConfigureAwait(false);

        var resultWorkflow = _raceResultWorkflowFactory(session);
        var results = new List<RaceResultCollectionResult>();
        var savedRaceIds = new List<string>();

        foreach (var course in courses.Where(x => x != RaceCourse.Unknown))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ハング調査用: 1競馬場ぶんの一覧取得にかかった時間をログに残す。
            // 手動E2Eで観測された「CPU使用率0%のまま無進行」がどの競馬場・
            // どの経路（Current/Recent/Historical）で発生しているかを
            // 切り分けるための診断ログであり、恒久的な最適化を意図したものではない。
            var courseStopwatch = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation(
                "[Diag] 成績収集: 競馬場ぶん一覧取得を開始します。Date={Date} Course={Course}",
                raceDate, course);

            Scraping.Jra.Pages.IJraPage listPage;
            try
            {
                // NOTE(過去月遷移バグ修正): 以前はここで出馬表専用の ToRaceListAsync を
                // 使っており、その開催選択ページ（/JRADB/accessD.html）は今週～直近数週間
                // 程度しか掲載されないため、月をまたぐ過去日を渡すと JraNavigationException
                // で失敗していた。成績収集では Current/Recent/Historical のルート分岐に
                // 対応した ToRaceResultListAsync を使う。
                listPage = await session.Navigate.ToRaceResultListAsync(raceDate, course, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "[Diag] 成績収集: 競馬場ぶん一覧取得が完了しました。Date={Date} Course={Course} ElapsedMs={ElapsedMs} Kind={Kind}",
                    raceDate, course, courseStopwatch.ElapsedMilliseconds, listPage.Kind);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "[Diag] 成績収集: 競馬場ぶん一覧取得が例外で終了しました。Date={Date} Course={Course} ElapsedMs={ElapsedMs} ExceptionType={ExceptionType}",
                    raceDate, course, courseStopwatch.ElapsedMilliseconds, ex.GetType().Name);
                // 1競馬場のレース一覧取得失敗で日付全体の処理を止めない。他の競馬場の
                // 処理は継続する（この日付・競馬場分の成績はジョブレベルではエラーとして
                // ログのみ記録し、次回サイクルでの再試行に委ねる）。
                _logger.LogWarning(
                    ex,
                    "[収集実行] レース一覧の取得に失敗しました（この競馬場の成績収集をスキップし、他の競馬場の処理を継続します）。Date={Date} Course={Course}",
                    raceDate, course);
                continue;
            }

            if (listPage is Scraping.Jra.Pages.JraRaceResultPage singleResult)
            {
                // 過去レース結果検索経由で、重賞レースなど開催選択ボタンが直接
                // 「レース結果」ページへリンクされているケース（詳細はJraNavigatorの
                // ToHistoricalRaceResultListAsync参照）。この日・競馬場に他のレースが
                // あっても番号を列挙できないため、判明している1レースのみ処理する。
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var singleRaceResult = await resultWorkflow
                        .CollectAsync(singleResult.RaceId, cancellationToken)
                        .ConfigureAwait(false);
                    results.Add(singleRaceResult);
                    if (singleRaceResult.SavedHorseNumbers.Count > 0)
                    {
                        savedRaceIds.Add(singleRaceResult.DataCollectionRaceId);
                    }
                }
                catch (JraCollectionException ex)
                {
                    _logger.LogInformation(
                        ex,
                        "[収集実行] 成績ページ未取得のためスキップします。Date={Date} Course={Course} RaceNumber={RaceNumber}",
                        raceDate, course, singleResult.RaceId.Number);
                }

                continue;
            }

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

                var raceStopwatch = System.Diagnostics.Stopwatch.StartNew();
                RaceResultCollectionResult result;
                try
                {
                    result = await resultWorkflow
                        .CollectAsync(new RaceId(raceDate, course, race.Number), cancellationToken)
                        .ConfigureAwait(false);
                    _logger.LogInformation(
                        "[Diag] 成績収集: 1レースぶんの取得が完了しました。Date={Date} Course={Course} RaceNumber={RaceNumber} ElapsedMs={ElapsedMs}",
                        raceDate, course, race.Number, raceStopwatch.ElapsedMilliseconds);
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

    /// <summary>
    /// 収集ジョブ1件分の処理に対する安全策タイムアウトを持つ、外側の
    /// <paramref name="cancellationToken"/> にリンクした <see cref="CancellationTokenSource"/> を作成する。
    ///
    /// 背景（手動E2E検証で判明したハング事象）: JraNavigator経由のブラウザ操作は、
    /// PlaywrightのAPI呼び出し単位では既定タイムアウト（既定30秒）で保護されているが、
    /// ページ内の要素走査がPlaywright側の1操作としては完了しつつ全体としては極めて
    /// 大量の逐次round-trip（例: 開催選択ページの候補要素を1件ずつawaitする実装）になる
    /// ケースでは、CPU使用率0%のままジョブ全体が長時間（観測値で9分以上）無進行に見える
    /// ことがある。呼び出し元（本サービス）にはジョブ単位のタイムアウトが存在しなかった
    /// ため、これが発生すると収集サイクル全体が実質的に無限に停止し得た。
    /// この安全策は根本原因（走査の遅さ自体）を解消するものではなく、既定のリース期限
    /// （<see cref="AgentProcessingOptions.CollectionLeaseMinutes"/>）より短い時間で
    /// ジョブを打ち切り、失敗として次回サイクルでの再試行に委ねることで、
    /// サービス全体の停止を防ぐことを目的とする。
    /// </summary>
    private CancellationTokenSource CreateJobTimeoutCts(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _options.CollectionJobTimeoutMinutes)));
        return cts;
    }

    /// <summary>
    /// 捕捉した例外が、外側のキャンセル（サービス停止等）ではなく
    /// <see cref="CreateJobTimeoutCts"/> によるジョブ単位タイムアウトによるものかどうかを判定する。
    /// </summary>
    private static bool IsJobTimeout(
        Exception ex,
        CancellationTokenSource jobTimeoutCts,
        CancellationToken outerCancellationToken)
        => ex is OperationCanceledException
            && jobTimeoutCts.IsCancellationRequested
            && !outerCancellationToken.IsCancellationRequested;

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
