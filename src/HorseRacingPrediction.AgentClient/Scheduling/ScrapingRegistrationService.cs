using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Extensions.AI;
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
    private readonly IWebBrowserSessionFactory _browserSessionFactory;
    private readonly IChatClient _chatClient;
    private readonly WebFetchOptions _webFetchOptions;
    private readonly PageDataExtractionAgent? _pageDataExtractionAgent;
    private readonly DataCollectionWriteTools _writeTools;
    private readonly ProcessingStateStore _stateStore;
    private readonly RaceTextInsightCollector _insightCollector;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ScrapingRegistrationService> _logger;

    public ScrapingRegistrationService(
        IOptions<AgentProcessingOptions> options,
        JraRaceScheduleCollectionWorkflow scheduleCollectionWorkflow,
        IWebBrowserSessionFactory browserSessionFactory,
        IChatClient chatClient,
        IOptions<WebFetchOptions> webFetchOptions,
        PageDataExtractionAgent? pageDataExtractionAgent,
        DataCollectionWriteTools writeTools,
        ProcessingStateStore stateStore,
        RaceTextInsightCollector insightCollector,
        ILoggerFactory loggerFactory,
        ILogger<ScrapingRegistrationService> logger)
    {
        _options = options.Value;
        _scheduleCollectionWorkflow = scheduleCollectionWorkflow;
        _browserSessionFactory = browserSessionFactory;
        _chatClient = chatClient;
        _webFetchOptions = webFetchOptions.Value;
        _pageDataExtractionAgent = pageDataExtractionAgent;
        _writeTools = writeTools;
        _stateStore = stateStore;
        _insightCollector = insightCollector;
        _loggerFactory = loggerFactory;
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
        var predictionCandidateRaceIds = new List<string>();
        JraRaceScheduleCollectionResult? schedule = null;

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
            }
        }

        if (_options.EnableRaceCardCollection
            && schedule is not null
            && string.IsNullOrWhiteSpace(schedule.Error))
        {
            foreach (var date in schedule.UpcomingRaceDates.Distinct().OrderBy(x => x))
            {
                _logger.LogInformation("[収集登録] 出馬表収集開始: {Date}", date);
                var result = await CollectRaceCardsAsync(date, cancellationToken).ConfigureAwait(false);
                predictionCandidateRaceIds.AddRange(result.SavedRaceIds);

                _logger.LogInformation(
                    "[収集登録] 出馬表収集完了: Date={Date} Saved={Saved} Errors={Errors}",
                    date,
                    result.SavedRaceIds.Count,
                    result.Errors.Count);

                foreach (var error in result.Errors)
                {
                    _logger.LogWarning("[収集登録] {Error}", error);
                }
            }
        }

        if (_options.EnableRaceResultCollection)
        {
            var start = today.AddDays(-Math.Max(0, _options.ResultLookbackDays));
            var end = today.AddDays(Math.Max(0, _options.ResultLookaheadDays));

            for (var date = start; date <= end; date = date.AddDays(1))
            {
                _logger.LogInformation("[収集登録] 成績収集開始: {Date}", date);
                var result = await CollectRaceResultsAsync(date, cancellationToken).ConfigureAwait(false);

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
        }

        var distinctRaceIds = BuildPredictionCandidateRaceIds(predictionCandidateRaceIds);
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

    private async Task<JraRaceCardCollectionResult> CollectRaceCardsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken)
    {
        await using var browser = await _browserSessionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

        var tools = new PlaywrightTools(
            browser,
            Options.Create(_webFetchOptions),
            _pageDataExtractionAgent,
            _loggerFactory.CreateLogger<PlaywrightTools>());
        var workflow = new JraRaceCardCollectionWorkflow(
            _chatClient,
            tools.GetReadPageOnlyAITools(),
            new JraRaceCardScraper(browser),
            _writeTools);

        return await workflow.CollectAsync(raceDate, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JraRaceResultCollectionResult> CollectRaceResultsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken)
    {
        await using var browser = await _browserSessionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

        var workflow = new JraRaceResultCollectionWorkflow(
            browser,
            new JraRaceResultScraper(browser),
            _writeTools,
            _loggerFactory.CreateLogger<JraRaceResultCollectionWorkflow>(),
            _loggerFactory);

        return await workflow.CollectAsync(raceDate, cancellationToken).ConfigureAwait(false);
    }

    internal static IReadOnlyList<string> BuildPredictionCandidateRaceIds(IEnumerable<string> savedRaceIds)
        => savedRaceIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
