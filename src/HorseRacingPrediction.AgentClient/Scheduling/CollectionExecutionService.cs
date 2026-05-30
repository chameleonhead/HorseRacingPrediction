using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Contracts;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using HorseRacingPrediction.Agents.Workflow;
using HorseRacingPrediction.Agents.JraAgent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class CollectionExecutionService : BackgroundService
{
    private static readonly string JraProviderType = "JRA";
    private static readonly string[] RecoverableJobTypes =
    [
        AgentJobType.RaceCardCollection,
        AgentJobType.ResultMonthDiscoveryRequest,
        AgentJobType.ResultDayDiscoveryRequest,
        AgentJobType.ResultDayCollectionRequest,
        AgentJobType.RaceResultCollection,
        AgentJobType.ResultBackfillPlanningRequest
    ];

    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");

    private readonly AgentProcessingOptions _options;
    private readonly ProcessingStateStore _stateStore;
    private readonly IWebBrowserSessionFactory _browserSessionFactory;
    private readonly IChatClient _chatClient;
    private readonly WebFetchOptions _webFetchOptions;
    private readonly PageDataExtractionAgent? _pageDataExtractionAgent;
    private readonly DataCollectionWriteTools _writeTools;
    private readonly IRaceQueryService _raceQueryService;
    private readonly IJraResultDateDiscoveryService _resultDateDiscoveryService;
    private readonly HistoricalDataRequestPlanner _historicalDataRequestPlanner;
    private readonly RaceTextInsightCollector _insightCollector;
    private readonly CollectionExecutionTrigger _executionTrigger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CollectionExecutionService> _logger;

    public CollectionExecutionService(
        IOptions<AgentProcessingOptions> options,
        ProcessingStateStore stateStore,
        IWebBrowserSessionFactory browserSessionFactory,
        IChatClient chatClient,
        IOptions<WebFetchOptions> webFetchOptions,
        PageDataExtractionAgent? pageDataExtractionAgent,
        DataCollectionWriteTools writeTools,
        IRaceQueryService raceQueryService,
        IJraResultDateDiscoveryService resultDateDiscoveryService,
        HistoricalDataRequestPlanner historicalDataRequestPlanner,
        RaceTextInsightCollector insightCollector,
        CollectionExecutionTrigger executionTrigger,
        ILoggerFactory loggerFactory,
        ILogger<CollectionExecutionService> logger)
    {
        _options = options.Value;
        _stateStore = stateStore;
        _browserSessionFactory = browserSessionFactory;
        _chatClient = chatClient;
        _webFetchOptions = webFetchOptions.Value;
        _pageDataExtractionAgent = pageDataExtractionAgent;
        _writeTools = writeTools;
        _raceQueryService = raceQueryService;
        _resultDateDiscoveryService = resultDateDiscoveryService;
        _historicalDataRequestPlanner = historicalDataRequestPlanner;
        _insightCollector = insightCollector;
        _executionTrigger = executionTrigger;
        _loggerFactory = loggerFactory;
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

    private async Task RunOneCycleAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await ExecuteRaceCardJobsAsync(now, cancellationToken).ConfigureAwait(false);
        await ExecuteResultMonthDiscoveryJobsAsync(now, cancellationToken).ConfigureAwait(false);
        await ExecuteResultDayDiscoveryJobsAsync(now, cancellationToken).ConfigureAwait(false);
        await ExecuteResultDayCollectionJobsAsync(now, cancellationToken).ConfigureAwait(false);
        await ExecuteRaceResultJobsAsync(now, cancellationToken).ConfigureAwait(false);
        await ExecuteResultBackfillPlanningJobsAsync(now, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteResultBackfillPlanningJobsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = await _stateStore
            .AcquireReadyJobsAsync(
                AgentJobType.ResultBackfillPlanningRequest,
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
                var payload = AgentJobPayloadSerializer.Deserialize<ResultBackfillPlanningRequestPayload>(job.Payload);
                var currentMonth = new DateOnly(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1);
                var oldestMonth = currentMonth.AddYears(-Math.Max(1, payload.InitialBackfillYears));
                var lastHistoricalMonth = currentMonth.AddMonths(-2);

                for (var month = new DateOnly(oldestMonth.Year, oldestMonth.Month, 1);
                     month <= lastHistoricalMonth;
                     month = month.AddMonths(1))
                {
                    var monthPayload = new ResultMonthDiscoveryRequestPayload(
                        payload.ProviderType,
                        month.Year,
                        month.Month,
                        RevisitIncompleteDays: false);
                    await _stateStore.EnqueueJobAsync(
                        AgentJobType.ResultMonthDiscoveryRequest,
                        AgentJobKeyFactory.BuildResultMonthDiscoveryRequestKey(payload.ProviderType, month.Year, month.Month),
                        AgentJobPayloadSerializer.Serialize(monthPayload),
                        now,
                        priority: 60,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                await _stateStore.CompleteJobAsync(AgentJobType.ResultBackfillPlanningRequest, job.DeduplicationKey, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[収集実行] バックフィル計画ジョブ失敗。JobKey={JobKey}", job.DeduplicationKey);
                await _stateStore.RequeueJobAsync(
                    AgentJobType.ResultBackfillPlanningRequest,
                    job.DeduplicationKey,
                    now,
                    ex.Message,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteResultMonthDiscoveryJobsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = await _stateStore
            .AcquireReadyJobsAsync(
                AgentJobType.ResultMonthDiscoveryRequest,
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
                var payload = AgentJobPayloadSerializer.Deserialize<ResultMonthDiscoveryRequestPayload>(job.Payload);
                var todayJst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Jst).Date);
                var monthDates = await _resultDateDiscoveryService
                    .DiscoverMonthDatesAsync(payload.Year, payload.Month, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var date in monthDates.Where(x => x <= todayJst))
                {
                    var currentStatus = await _stateStore
                        .GetResultDayCollectionStatusAsync(payload.ProviderType, date, cancellationToken)
                        .ConfigureAwait(false);
                    if (currentStatus?.Status == ResultDayCollectionState.Complete)
                    {
                        continue;
                    }

                    if (currentStatus?.RetryAfter is { } retryAfter && retryAfter > now)
                    {
                        continue;
                    }

                    var dayPayload = new ResultDayDiscoveryRequestPayload(date, payload.ProviderType);
                    await _stateStore.ScheduleJobAsync(
                        AgentJobType.ResultDayDiscoveryRequest,
                        AgentJobKeyFactory.BuildResultDayDiscoveryRequestKey(payload.ProviderType, date),
                        AgentJobPayloadSerializer.Serialize(dayPayload),
                        now,
                        priority: date >= todayJst.AddMonths(-1) ? 150 : 70,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                await _stateStore.CompleteJobAsync(AgentJobType.ResultMonthDiscoveryRequest, job.DeduplicationKey, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[収集実行] 月次成績探索ジョブ失敗。JobKey={JobKey}", job.DeduplicationKey);
                await _stateStore.RequeueJobAsync(
                    AgentJobType.ResultMonthDiscoveryRequest,
                    job.DeduplicationKey,
                    now,
                    ex.Message,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteResultDayDiscoveryJobsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = await _stateStore
            .AcquireReadyJobsAsync(
                AgentJobType.ResultDayDiscoveryRequest,
                now,
                TimeSpan.Zero,
                _options.CollectionBatchSize,
                TimeSpan.FromMinutes(Math.Max(1, _options.CollectionLeaseMinutes)),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var job in jobs)
        {
            var payload = AgentJobPayloadSerializer.Deserialize<ResultDayDiscoveryRequestPayload>(job.Payload);

            try
            {
                await _stateStore.UpsertResultDayCollectionStatusAsync(
                    payload.ProviderType,
                    payload.RaceDate,
                    ResultDayCollectionState.Discovering,
                    expectedRaceCount: null,
                    completedRaceCount: null,
                    incompleteReason: null,
                    lastCompletedAt: null,
                    retryAfter: null,
                    lastError: null,
                    now,
                    cancellationToken).ConfigureAwait(false);

                var discoveredUrls = await DiscoverResultUrlsAsync(payload.RaceDate, cancellationToken).ConfigureAwait(false);
                if (discoveredUrls.Count == 0)
                {
                    var shouldRetry = ShouldRetryIncompleteDay(payload.RaceDate, now);
                    await _stateStore.UpsertResultDayCollectionStatusAsync(
                        payload.ProviderType,
                        payload.RaceDate,
                        shouldRetry ? ResultDayCollectionState.RetryScheduled : ResultDayCollectionState.Complete,
                        expectedRaceCount: 0,
                        completedRaceCount: 0,
                        incompleteReason: shouldRetry ? "Result URL discovery returned no URLs." : null,
                        lastCompletedAt: shouldRetry ? null : now,
                        retryAfter: shouldRetry ? now.AddHours(3) : null,
                        lastError: shouldRetry ? "Result URL discovery returned no URLs." : null,
                        now,
                        cancellationToken).ConfigureAwait(false);

                    await _stateStore.CompleteJobAsync(AgentJobType.ResultDayDiscoveryRequest, job.DeduplicationKey, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                var urls = await FilterUnregisteredResultUrlsAsync(discoveredUrls, payload.RaceDate, cancellationToken).ConfigureAwait(false);
                if (urls.Count == 0)
                {
                    await _stateStore.UpsertResultDayCollectionStatusAsync(
                        payload.ProviderType,
                        payload.RaceDate,
                        ResultDayCollectionState.Complete,
                        expectedRaceCount: discoveredUrls.Count,
                        completedRaceCount: discoveredUrls.Count,
                        incompleteReason: null,
                        lastCompletedAt: now,
                        retryAfter: null,
                        lastError: null,
                        now,
                        cancellationToken).ConfigureAwait(false);

                    await _stateStore.CompleteJobAsync(AgentJobType.ResultDayDiscoveryRequest, job.DeduplicationKey, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                var dayCollectionPayload = new ResultDayCollectionRequestPayload(
                    payload.RaceDate,
                    payload.ProviderType,
                    urls,
                    urls.Count);
                await _stateStore.ScheduleJobAsync(
                    AgentJobType.ResultDayCollectionRequest,
                    AgentJobKeyFactory.BuildResultDayCollectionRequestKey(payload.ProviderType, payload.RaceDate),
                    AgentJobPayloadSerializer.Serialize(dayCollectionPayload),
                    now,
                    priority: 140,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await _stateStore.UpsertResultDayCollectionStatusAsync(
                    payload.ProviderType,
                    payload.RaceDate,
                    ResultDayCollectionState.Ready,
                    expectedRaceCount: urls.Count,
                    completedRaceCount: 0,
                    incompleteReason: null,
                    lastCompletedAt: null,
                    retryAfter: null,
                    lastError: null,
                    now,
                    cancellationToken).ConfigureAwait(false);

                await _stateStore.CompleteJobAsync(AgentJobType.ResultDayDiscoveryRequest, job.DeduplicationKey, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[収集実行] 日次成績探索ジョブ失敗。Date={Date}", payload.RaceDate);
                await _stateStore.UpsertResultDayCollectionStatusAsync(
                    payload.ProviderType,
                    payload.RaceDate,
                    ResultDayCollectionState.RetryScheduled,
                    expectedRaceCount: null,
                    completedRaceCount: null,
                    incompleteReason: ex.Message,
                    lastCompletedAt: null,
                    retryAfter: now.AddHours(3),
                    lastError: ex.Message,
                    now,
                    cancellationToken).ConfigureAwait(false);
                await _stateStore.RequeueJobAsync(
                    AgentJobType.ResultDayDiscoveryRequest,
                    job.DeduplicationKey,
                    now,
                    ex.Message,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteResultDayCollectionJobsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var jobs = await _stateStore
            .AcquireReadyJobsAsync(
                AgentJobType.ResultDayCollectionRequest,
                now,
                TimeSpan.Zero,
                _options.CollectionBatchSize,
                TimeSpan.FromMinutes(Math.Max(1, _options.CollectionLeaseMinutes)),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var job in jobs)
        {
            var payload = AgentJobPayloadSerializer.Deserialize<ResultDayCollectionRequestPayload>(job.Payload);

            try
            {
                await _stateStore.UpsertResultDayCollectionStatusAsync(
                    payload.ProviderType,
                    payload.RaceDate,
                    ResultDayCollectionState.Running,
                    expectedRaceCount: payload.ExpectedRaceCount,
                    completedRaceCount: 0,
                    incompleteReason: null,
                    lastCompletedAt: null,
                    retryAfter: null,
                    lastError: null,
                    now,
                    cancellationToken).ConfigureAwait(false);

                var result = await CollectRaceResultsAsync(payload.RaceDate, payload.Urls, cancellationToken).ConfigureAwait(false);
                await RecordScheduledRaceResultStatusesAsync(result, now, cancellationToken).ConfigureAwait(false);

                var completedRaceCount = result.SavedRaceIds.Count;
                var expectedRaceCount = payload.ExpectedRaceCount;
                var isComplete = completedRaceCount >= expectedRaceCount && result.Errors.Count == 0;
                var shouldRetry = ShouldRetryIncompleteDay(payload.RaceDate, now);

                await _stateStore.UpsertResultDayCollectionStatusAsync(
                    payload.ProviderType,
                    payload.RaceDate,
                    isComplete
                        ? ResultDayCollectionState.Complete
                        : shouldRetry ? ResultDayCollectionState.RetryScheduled : ResultDayCollectionState.Incomplete,
                    expectedRaceCount,
                    completedRaceCount,
                    incompleteReason: result.Errors.Count == 0 ? null : string.Join(Environment.NewLine, result.Errors),
                    lastCompletedAt: isComplete ? now : null,
                    retryAfter: isComplete || !shouldRetry ? null : now.AddHours(3),
                    lastError: result.Errors.Count == 0 ? null : string.Join(Environment.NewLine, result.Errors),
                    now,
                    cancellationToken).ConfigureAwait(false);

                await _stateStore.CompleteJobAsync(AgentJobType.ResultDayCollectionRequest, job.DeduplicationKey, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[収集実行] 日次成績収集ジョブ失敗。Date={Date}", payload.RaceDate);
                await _stateStore.UpsertResultDayCollectionStatusAsync(
                    payload.ProviderType,
                    payload.RaceDate,
                    ResultDayCollectionState.RetryScheduled,
                    expectedRaceCount: payload.ExpectedRaceCount,
                    completedRaceCount: null,
                    incompleteReason: ex.Message,
                    lastCompletedAt: null,
                    retryAfter: now.AddHours(3),
                    lastError: ex.Message,
                    now,
                    cancellationToken).ConfigureAwait(false);
                await _stateStore.RequeueJobAsync(
                    AgentJobType.ResultDayCollectionRequest,
                    job.DeduplicationKey,
                    now,
                    ex.Message,
                    cancellationToken).ConfigureAwait(false);
            }
        }
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

                var result = await CollectRaceCardsAsync(payload.RaceDate, cancellationToken).ConfigureAwait(false);

                if (ShouldRetryRaceCardCollection(payload.RaceDate, result, now))
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

                await RecordRaceCardStatusesAsync(result, now, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "[収集実行] 出馬表収集完了: Date={Date} Saved={Saved} Errors={Errors}",
                    payload.RaceDate,
                    result.SavedRaceIds.Count,
                    result.Errors.Count);

                foreach (var error in result.Errors)
                {
                    _logger.LogWarning("[収集実行] {Error}", error);
                }

                var distinctRaceIds = ScrapingRegistrationService.BuildPredictionCandidateRaceIds(result.SavedRaceIds);
                if (distinctRaceIds.Count > 0)
                {
                    foreach (var raceId in distinctRaceIds)
                    {
                        var plan = await _historicalDataRequestPlanner
                            .EnsureRequestsForRaceAsync(raceId, now, cancellationToken)
                            .ConfigureAwait(false);

                        if (plan.RequestedRaceResultCount > 0)
                        {
                            _logger.LogInformation(
                            "[収集実行] 過去レース結果取得要求を登録しました。RaceId={RaceId} RaceResultRequests={RaceResultRequests}",
                                raceId,
                            plan.RequestedRaceResultCount);
                        }
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

                await _stateStore.CompleteJobAsync(AgentJobType.RaceCardCollection, job.DeduplicationKey, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[収集実行] 出馬表収集ジョブ失敗。JobKey={JobKey}", job.DeduplicationKey);
                await _stateStore.RequeueJobAsync(
                    AgentJobType.RaceCardCollection,
                    job.DeduplicationKey,
                    now,
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

                var result = await CollectRaceResultsAsync(payload.RaceDate, cancellationToken).ConfigureAwait(false);
                await RecordScheduledRaceResultStatusesAsync(result, now, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "[収集実行] 成績収集完了: Date={Date} Saved={Saved} Errors={Errors}",
                    payload.RaceDate,
                    result.SavedRaceIds.Count,
                    result.Errors.Count);

                foreach (var error in result.Errors)
                {
                    _logger.LogWarning("[収集実行] {Error}", error);
                }

                await _stateStore.CompleteJobAsync(AgentJobType.RaceResultCollection, job.DeduplicationKey, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[収集実行] 成績収集ジョブ失敗。JobKey={JobKey}", job.DeduplicationKey);
                await _stateStore.RequeueJobAsync(
                    AgentJobType.RaceResultCollection,
                    job.DeduplicationKey,
                    now,
                    ex.Message,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<JraRaceCardCollectionResult> CollectRaceCardsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken)
    {
        await using var browser = await _browserSessionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

        var workflow = new JraRaceCardCollectionWorkflow(
            browser,
            new JraRaceCardScraper(browser),
            _writeTools);

        return await workflow.CollectAsync(raceDate, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JraRaceResultCollectionResult> CollectRaceResultsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken)
    {
        var urls = await DiscoverUnregisteredResultUrlsAsync(raceDate, cancellationToken).ConfigureAwait(false);
        return await CollectRaceResultsAsync(raceDate, urls, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JraRaceResultCollectionResult> CollectRaceResultsAsync(
        DateOnly raceDate,
        IReadOnlyList<JraRaceResultUrl> urls,
        CancellationToken cancellationToken)
    {
        await using var browser = await _browserSessionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

        var workflow = CreateRaceResultWorkflow(browser);
        var scraped = await workflow.ScrapeAllAsync(urls, cancellationToken).ConfigureAwait(false);
        var (savedRaceIds, errors) = await workflow.SaveAllAsync(scraped, cancellationToken).ConfigureAwait(false);

        return new JraRaceResultCollectionResult(
            RaceDate: raceDate,
            DiscoveredUrls: urls,
            ScrapedResults: scraped.Select(x => x.Data).ToList(),
            SavedRaceIds: savedRaceIds,
            Errors: errors);
    }

    private async Task<IReadOnlyList<JraRaceResultUrl>> DiscoverResultUrlsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken)
    {
        await using var browser = await _browserSessionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        var workflow = CreateRaceResultWorkflow(browser);
        return await workflow.DiscoverUrlsAsync(raceDate, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<JraRaceResultUrl>> DiscoverUnregisteredResultUrlsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken)
    {
        var discoveredUrls = await DiscoverResultUrlsAsync(raceDate, cancellationToken).ConfigureAwait(false);
        return await FilterUnregisteredResultUrlsAsync(discoveredUrls, raceDate, cancellationToken).ConfigureAwait(false);
    }

    private JraRaceResultCollectionWorkflow CreateRaceResultWorkflow(IWebBrowser browser)
        => new(
            browser,
            new JraRaceResultScraper(browser),
            _writeTools,
            _raceQueryService,
            _loggerFactory.CreateLogger<JraRaceResultCollectionWorkflow>(),
            _loggerFactory);

    private static bool ShouldRetryIncompleteDay(DateOnly raceDate, DateTimeOffset now)
    {
        var todayJst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Jst).Date);
        var retryThreshold = new DateOnly(todayJst.Year, todayJst.Month, 1).AddMonths(-1);
        return raceDate >= retryThreshold;
    }

    private static bool ShouldRetryRaceCardCollection(
        DateOnly raceDate,
        JraRaceCardCollectionResult result,
        DateTimeOffset now)
    {
        if (result.DiscoveredUrls.Count > 0 || result.Errors.Count > 0)
        {
            return false;
        }

        var todayJst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Jst).Date);
        return raceDate >= todayJst;
    }

    private async Task<IReadOnlyList<JraRaceResultUrl>> FilterUnregisteredResultUrlsAsync(
        IReadOnlyList<JraRaceResultUrl> urls,
        DateOnly raceDate,
        CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
        {
            return urls;
        }

        var registeredRaces = await _raceQueryService
            .SearchRegisteredRacesAsync(raceDate, cancellationToken)
            .ConfigureAwait(false);

        var registeredRaceIdsByKey = registeredRaces
            .Select(x => new { Key = BuildResultRaceKey(x), x.RaceId })
            .Where(x => x.Key is not null && !string.IsNullOrWhiteSpace(x.RaceId))
            .ToDictionary(x => x.Key!, x => x.RaceId, StringComparer.Ordinal);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var filtered = new List<JraRaceResultUrl>(urls.Count);

        foreach (var url in urls)
        {
            var key = BuildResultRaceKey(url);
            if (key is not null)
            {
                if (!seenKeys.Add(key))
                {
                    continue;
                }

                if (registeredRaceIdsByKey.TryGetValue(key, out var raceId))
                {
                    var context = await _raceQueryService
                        .GetRacePredictionContextAsync(raceId, cancellationToken)
                        .ConfigureAwait(false);
                    if (HasRequiredResultMetadata(context))
                    {
                        continue;
                    }
                }
            }

            filtered.Add(url);
        }

        return filtered;
    }

    private static string? BuildResultRaceKey(RaceSearchSummary summary)
    {
        if (summary.RaceDate is null || summary.RaceNumber is null || string.IsNullOrWhiteSpace(summary.RacecourseCode))
        {
            return null;
        }

        var racecourseKey = NormalizeRacecourseKey(summary.RacecourseCode);
        return racecourseKey is null
            ? null
            : $"{summary.RaceDate:yyyy-MM-dd}|{racecourseKey}|{summary.RaceNumber.Value:D2}";
    }

    private static string? BuildResultRaceKey(JraRaceResultUrl url)
    {
        if (url.RaceDate is null || url.RaceNumber is null)
        {
            return null;
        }

        var racecourse = !string.IsNullOrWhiteSpace(url.Racecourse)
            ? url.Racecourse
            : url.RacecourseCode;

        if (string.IsNullOrWhiteSpace(racecourse))
        {
            return null;
        }

        var racecourseKey = NormalizeRacecourseKey(racecourse);
        return racecourseKey is null
            ? null
            : $"{url.RaceDate:yyyy-MM-dd}|{racecourseKey}|{url.RaceNumber.Value:D2}";
    }

    private static string? NormalizeRacecourseKey(string? racecourse)
    {
        var displayName = JraRacecourseResolver.ResolveDisplayName(racecourse);
        return string.IsNullOrWhiteSpace(displayName)
            ? null
            : DeterministicIdGenerator.NormalizeKey(displayName);
    }

    private async Task RecordRaceCardStatusesAsync(
        JraRaceCardCollectionResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var errorsByUrl = BuildErrorLookup(result.Errors);
        var scrapedByKey = result.ScrapedCards
            .Where(x => x.RaceDate is not null && x.RaceNumber is not null && !string.IsNullOrWhiteSpace(x.Racecourse))
            .ToDictionary(
                x => RaceDataCollectionKeyFactory.Build(x.RaceDate!.Value, x.Racecourse!, x.RaceNumber!.Value),
                x => x,
                StringComparer.Ordinal);
        var savedIds = result.SavedRaceIds.ToHashSet(StringComparer.Ordinal);

        foreach (var source in result.DiscoveredUrls)
        {
            if (!TryResolveRaceIdentity(source.RaceDate, source.Racecourse ?? source.RacecourseCode, source.RaceNumber, out var raceDate, out var racecourse, out var raceNumber))
            {
                continue;
            }

            var raceKey = RaceDataCollectionKeyFactory.Build(raceDate, racecourse, raceNumber);
            scrapedByKey.TryGetValue(raceKey, out var scraped);
            var raceId = DeterministicIdGenerator.BuildRaceId(raceDate, racecourse, raceNumber);
            var explicitError = errorsByUrl.TryGetValue(source.Url, out var error) ? error : null;

            RaceDataCollectionState status;
            RaceDataCollectionErrorCode? errorCode = null;
            string? errorReason = null;

            if (savedIds.Contains(raceId))
            {
                status = RaceDataCollectionState.Succeeded;
            }
            else if (explicitError is not null)
            {
                status = RaceDataCollectionState.Failed;
                var classified = RaceDataCollectionErrorClassifier.Classify(explicitError);
                errorCode = classified.Code;
                errorReason = explicitError;
            }
            else if (scraped is null)
            {
                status = RaceDataCollectionState.Failed;
                var classified = RaceDataCollectionErrorClassifier.Classify("No card data was extracted for the discovered URL.");
                errorCode = classified.Code;
                errorReason = classified.Reason;
            }
            else
            {
                status = RaceDataCollectionState.Failed;
                var classified = RaceDataCollectionErrorClassifier.Classify("Card data was scraped but could not be saved.");
                errorCode = classified.Code;
                errorReason = classified.Reason;
            }

            await _stateStore.UpsertRaceCardCollectionStatusAsync(
                raceDate,
                racecourse,
                raceNumber,
                savedIds.Contains(raceId) ? raceId : null,
                scraped?.RaceName,
                source.Url,
                status,
                errorCode,
                errorReason,
                now,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RecordScheduledRaceResultStatusesAsync(
        JraRaceResultCollectionResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var errorsByUrl = BuildErrorLookup(result.Errors);
        var scrapedByKey = result.ScrapedResults
            .Where(x => x.RaceDate is not null && x.RaceNumber is not null && !string.IsNullOrWhiteSpace(x.Racecourse))
            .GroupBy(
                x => RaceDataCollectionKeyFactory.Build(x.RaceDate!.Value, x.Racecourse!, x.RaceNumber!.Value),
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        var savedIds = result.SavedRaceIds.ToHashSet(StringComparer.Ordinal);

        foreach (var source in result.DiscoveredUrls)
        {
            var racecourseValue = source.Racecourse ?? source.RacecourseCode;
            if (!TryResolveRaceIdentity(source.RaceDate, racecourseValue, source.RaceNumber, out var raceDate, out var racecourse, out var raceNumber))
            {
                continue;
            }

            var raceKey = RaceDataCollectionKeyFactory.Build(raceDate, racecourse, raceNumber);
            scrapedByKey.TryGetValue(raceKey, out var scraped);
            var raceId = DeterministicIdGenerator.BuildRaceId(raceDate, racecourse, raceNumber);
            var explicitError = errorsByUrl.TryGetValue(source.Url, out var error) ? error : null;

            RaceDataCollectionState status;
            RaceDataCollectionErrorCode? errorCode = null;
            string? errorReason = null;

            if (savedIds.Contains(raceId))
            {
                status = RaceDataCollectionState.Succeeded;
            }
            else if (explicitError is not null)
            {
                status = RaceDataCollectionState.Failed;
                var classified = RaceDataCollectionErrorClassifier.Classify(explicitError);
                errorCode = classified.Code;
                errorReason = explicitError;
            }
            else if (scraped is null)
            {
                status = RaceDataCollectionState.Failed;
                var classified = RaceDataCollectionErrorClassifier.Classify("No result data was extracted for the discovered URL.");
                errorCode = classified.Code;
                errorReason = classified.Reason;
            }
            else
            {
                status = RaceDataCollectionState.Failed;
                var classified = RaceDataCollectionErrorClassifier.Classify("Race result data was scraped but could not be saved.");
                errorCode = classified.Code;
                errorReason = classified.Reason;
            }

            await _stateStore.UpsertRaceResultCollectionStatusAsync(
                raceDate,
                racecourse,
                raceNumber,
                savedIds.Contains(raceId) ? raceId : null,
                scraped?.RaceName,
                source.Url,
                status,
                RaceResultAcquisitionOrigin.Scheduled,
                requestedByRaceId: null,
                errorCode,
                errorReason,
                now,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> BuildErrorLookup(IReadOnlyList<string> errors)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var error in errors)
        {
            var separatorIndex = error.IndexOf(" — ", StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var prefix = error[..separatorIndex];
            var urlSeparatorIndex = prefix.IndexOf(':', StringComparison.Ordinal);
            if (urlSeparatorIndex < 0 || urlSeparatorIndex + 1 >= prefix.Length)
            {
                continue;
            }

            var url = prefix[(urlSeparatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(url))
            {
                lookup[url] = error;
            }
        }

        return lookup;
    }

    private static bool TryResolveRaceIdentity(
        DateOnly? raceDate,
        string? racecourse,
        int? raceNumber,
        out DateOnly resolvedRaceDate,
        out string resolvedRacecourse,
        out int resolvedRaceNumber)
    {
        resolvedRaceDate = default;
        resolvedRacecourse = string.Empty;
        resolvedRaceNumber = default;

        var racecourseDisplayName = JraRacecourseResolver.ResolveDisplayName(racecourse);
        if (raceDate is null || string.IsNullOrWhiteSpace(racecourseDisplayName) || raceNumber is null)
        {
            return false;
        }

        resolvedRaceDate = raceDate.Value;
        resolvedRacecourse = racecourseDisplayName;
        resolvedRaceNumber = raceNumber.Value;
        return true;
    }

    private static bool HasRequiredResultMetadata(RacePredictionContextReadModel? context)
    {
        return context is not null
            && !string.IsNullOrWhiteSpace(context.SurfaceCode)
            && context.DistanceMeters is > 0
            && !string.IsNullOrWhiteSpace(context.GradeCode)
            && context.Status >= RaceStatus.ResultDeclared;
    }
}