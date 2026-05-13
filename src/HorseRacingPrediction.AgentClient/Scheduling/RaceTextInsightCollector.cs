using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HorseRacingPrediction.AgentClient.Http;
using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Scheduling;

public sealed class RaceTextInsightCollector
{
    private readonly AgentProcessingOptions _options;
    private readonly IRaceQueryService _raceQueryService;
    private readonly IMemoWriteService _memoWriteService;
    private readonly IWebBrowserSessionFactory _browserSessionFactory;
    private readonly IChatClient _chatClient;
    private readonly WebFetchOptions _webFetchOptions;
    private readonly PageDataExtractionAgent? _pageDataExtractionAgent;
    private readonly ProcessingStateStore _stateStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RaceTextInsightCollector> _logger;

    public RaceTextInsightCollector(
        IOptions<AgentProcessingOptions> options,
        IRaceQueryService raceQueryService,
        IMemoWriteService memoWriteService,
        IWebBrowserSessionFactory browserSessionFactory,
        IChatClient chatClient,
        IOptions<WebFetchOptions> webFetchOptions,
        PageDataExtractionAgent? pageDataExtractionAgent,
        ProcessingStateStore stateStore,
        ILoggerFactory loggerFactory,
        ILogger<RaceTextInsightCollector> logger)
    {
        _options = options.Value;
        _raceQueryService = raceQueryService;
        _memoWriteService = memoWriteService;
        _browserSessionFactory = browserSessionFactory;
        _chatClient = chatClient;
        _webFetchOptions = webFetchOptions.Value;
        _pageDataExtractionAgent = pageDataExtractionAgent;
        _stateStore = stateStore;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task CollectForRaceAsync(string raceId, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableTextInsightCollection)
        {
            return;
        }

        var context = await _raceQueryService.GetRacePredictionContextAsync(raceId, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            _logger.LogDebug("RacePredictionContext が見つからないため任意テキスト収集をスキップします。RaceId={RaceId}", raceId);
            return;
        }

        await using var browser = await _browserSessionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        var webBrowserAgent = CreateWebBrowserAgent(browser);

        foreach (var template in _options.TextInsightQueryTemplates.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var query = BuildQuery(template, context);
            var raceDate = context.RaceDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var insightKey = BuildInsightKey(raceId, query, raceDate);

            if (await _stateStore.IsTextInsightRecordedAsync(insightKey, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            try
            {
                var prompt = $"""
                    以下のテーマについて公開Web情報を収集し、事実ベースで簡潔な日本語メモを作成してください。
                    テーマ: {query}

                    出力ルール:
                    - 箇条書きで5項目以内
                    - 断定できない情報は『未確認』と明記
                    - 噂や根拠不明情報は除外
                    """;

                var memoText = await webBrowserAgent.InvokeAsync(prompt, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(memoText))
                {
                    continue;
                }

                var memoId = BuildDeterministicMemoId(raceId, query, raceDate);
                await _memoWriteService.CreateRaceMemoAsync(
                    raceId: raceId,
                    memoType: "ExternalTextInsight",
                    content: memoText,
                    authorId: "agent-client",
                    memoId: memoId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await _stateStore.MarkTextInsightRecordedAsync(insightKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "任意テキスト収集でエラーが発生しました。RaceId={RaceId} Query={Query}", raceId, query);
            }
        }
    }

    private WebBrowserAgent CreateWebBrowserAgent(IWebBrowser browser)
    {
        var tools = new PlaywrightTools(
            browser,
            Options.Create(_webFetchOptions),
            _pageDataExtractionAgent,
            _loggerFactory.CreateLogger<PlaywrightTools>());

        return new WebBrowserAgent(_chatClient, tools.GetAITools());
    }

    private static string BuildQuery(string template, RacePredictionContextReadModel context)
    {
        var raceDateText = context.RaceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ?? string.Empty;
        var raceNumberText = context.RaceNumber?.ToString(CultureInfo.InvariantCulture)
            ?? string.Empty;

        return template
            .Replace("{RaceDate}", raceDateText, StringComparison.Ordinal)
            .Replace("{RacecourseCode}", context.RacecourseCode ?? string.Empty, StringComparison.Ordinal)
            .Replace("{RaceNumber}", raceNumberText, StringComparison.Ordinal)
            .Replace("{RaceName}", context.RaceName ?? string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string BuildInsightKey(string raceId, string query, DateOnly raceDate)
    {
        var hash = Sha256(query);
        return $"{raceId}:{raceDate:yyyyMMdd}:{hash}";
    }

    private static string BuildDeterministicMemoId(string raceId, string query, DateOnly raceDate)
    {
        var hash = Sha256(query);
        return $"memo-{raceId}-{raceDate:yyyyMMdd}-{hash[..10]}";
    }

    private static string Sha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
