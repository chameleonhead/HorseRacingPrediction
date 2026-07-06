using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Scraping.JraNavigation;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Scrapers.Jra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorseRacingPrediction.Scraping.Workflow;

/// <summary>
/// JRA 成績データを収集して DB へ保存するワークフロー。
/// <para>
/// <see cref="JraRaceResultUrlDiscoverer"/> はブラウザ操作で成績 URL の「発見」のみを担当し、
/// 各ページの詳細スクレイピングは Playwright ベースの <see cref="JraRaceResultScraper"/> が行う。
/// 最後に <see cref="DataCollectionWriteTools"/> で EventFlow 経由でドメインへ保存する。
/// </para>
/// <para>
/// ワークフロー:
/// <list type="number">
///   <item><see cref="DiscoverUrlsAsync"/> — ブラウザ操作で JRA 成績一覧から URL を収集</item>
///   <item><see cref="ScrapeAllAsync"/> — 各 URL を Playwright で決定的にスクレイプ</item>
///   <item><see cref="SaveAllAsync"/> — スクレイプ結果を EventFlow コマンド経由で保存</item>
///   <item><see cref="CollectAsync"/> — 上記3ステップをまとめて実行</item>
/// </list>
/// </para>
/// </summary>
public sealed class JraRaceResultCollectionWorkflow
{
    private static readonly IReadOnlyDictionary<string, string> RacecourseAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["01"] = "札幌",
            ["02"] = "函館",
            ["03"] = "福島",
            ["04"] = "新潟",
            ["05"] = "東京",
            ["06"] = "中山",
            ["07"] = "中京",
            ["08"] = "京都",
            ["09"] = "阪神",
            ["10"] = "小倉",
            ["札幌"] = "札幌",
            ["函館"] = "函館",
            ["福島"] = "福島",
            ["新潟"] = "新潟",
            ["東京"] = "東京",
            ["中山"] = "中山",
            ["中京"] = "中京",
            ["京都"] = "京都",
            ["阪神"] = "阪神",
            ["小倉"] = "小倉",
        };

    private readonly JraRaceResultUrlDiscoverer _discoverer;
    private readonly JraRaceResultScraper _scraper;
    private readonly JraRaceCardScraper? _raceCardScraper;
    private readonly DataCollectionWriteTools _writeTools;
    private readonly IRaceQueryService? _queryService;
    private readonly ILogger<JraRaceResultCollectionWorkflow> _logger;

    internal JraRaceResultCollectionWorkflow(
        JraRaceResultUrlDiscoverer discoverer,
        JraRaceResultScraper scraper,
        JraRaceCardScraper? raceCardScraper,
        DataCollectionWriteTools writeTools,
        IRaceQueryService? queryService = null,
        ILogger<JraRaceResultCollectionWorkflow>? logger = null)
    {
        _discoverer = discoverer;
        _scraper = scraper;
        _raceCardScraper = raceCardScraper;
        _writeTools = writeTools;
        _queryService = queryService;
        _logger = logger ?? NullLogger<JraRaceResultCollectionWorkflow>.Instance;
    }

    public JraRaceResultCollectionWorkflow(
        IWebBrowser browser,
        JraRaceResultScraper scraper,
        DataCollectionWriteTools writeTools,
        ILogger<JraRaceResultCollectionWorkflow>? logger = null,
        ILoggerFactory? loggerFactory = null)
        : this(
            new JraRaceResultUrlDiscoverer(
                browser,
                loggerFactory?.CreateLogger<JraRaceResultUrlDiscoverer>()),
            scraper,
            new JraRaceCardScraper(browser),
            writeTools,
            queryService: null,
            logger)
    {
    }

    public JraRaceResultCollectionWorkflow(
        IWebBrowser browser,
        JraRaceResultScraper scraper,
        DataCollectionWriteTools writeTools,
        IRaceQueryService queryService,
        ILogger<JraRaceResultCollectionWorkflow>? logger = null,
        ILoggerFactory? loggerFactory = null)
        : this(
            new JraRaceResultUrlDiscoverer(
                browser,
                loggerFactory?.CreateLogger<JraRaceResultUrlDiscoverer>()),
            scraper,
            new JraRaceCardScraper(browser),
            writeTools,
            queryService,
            logger)
    {
    }

    /// <summary>
    /// 指定した開催日の成績 URL 一覧をブラウザ操作で発見して返す。
    /// </summary>
    public Task<IReadOnlyList<JraRaceResultUrl>> DiscoverUrlsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken = default)
        => _discoverer.DiscoverUrlsAsync(raceDate, cancellationToken);

    internal async Task<IReadOnlyList<JraRaceResultUrl>> FilterUnregisteredUrlsAsync(
        IReadOnlyList<JraRaceResultUrl> urls,
        DateOnly raceDate,
        CancellationToken cancellationToken = default)
    {
        if (_queryService is null || urls.Count == 0)
            return urls;

        var registeredRaces = await _queryService
            .SearchRegisteredRacesAsync(raceDate, cancellationToken)
            .ConfigureAwait(false);

        var registeredRaceIdsByKey = registeredRaces
            .Select(x => new { Key = BuildRaceKey(x), x.RaceId })
            .Where(x => x.Key is not null && !string.IsNullOrWhiteSpace(x.RaceId))
            .ToDictionary(x => x.Key!, x => x.RaceId, StringComparer.Ordinal);

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var filtered = new List<JraRaceResultUrl>(urls.Count);

        foreach (var url in urls)
        {
            var key = BuildRaceKey(url);
            if (key is not null)
            {
                if (!seenKeys.Add(key))
                    continue;

                if (registeredRaceIdsByKey.TryGetValue(key, out var raceId))
                {
                    var context = await _queryService
                        .GetRacePredictionContextAsync(raceId, cancellationToken)
                        .ConfigureAwait(false);
                    if (HasRequiredResultMetadata(context))
                        continue;
                }
            }

            filtered.Add(url);
        }

        return filtered;
    }

    /// <summary>
    /// 指定した URL 一覧を Playwright でスクレイプして成績データを返す。
    /// スクレイプに失敗した URL は結果から除外される。
    /// </summary>
    public async Task<IReadOnlyList<(JraRaceResultUrl Source, JraRaceResultData Data)>> ScrapeAllAsync(
        IReadOnlyList<JraRaceResultUrl> urls,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(JraRaceResultUrl, JraRaceResultData)>();

        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var data = await _scraper.ScrapeAsync(url.Url, cancellationToken);
            if (data is not null)
            {
                results.Add((url, data));
            }
        }

        return results;
    }

    /// <summary>
    /// スクレイプ結果を EventFlow 経由で DB へ保存し、保存に成功したレース ID を返す。
    /// 保存に失敗したエントリはエラーメッセージとして返される。
    /// </summary>
    public async Task<(IReadOnlyList<string> SavedRaceIds, IReadOnlyList<string> Errors)> SaveAllAsync(
        IReadOnlyList<(JraRaceResultUrl Source, JraRaceResultData Data)> scraped,
        CancellationToken cancellationToken = default)
    {
        var savedRaceIds = new List<string>();
        var errors = new List<string>();

        foreach (var (source, data) in scraped)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var raceId = await TrySaveResultAsync(source, data, cancellationToken);
                if (raceId is not null)
                {
                    savedRaceIds.Add(raceId);
                }
                else
                {
                    errors.Add(
                        $"保存スキップ: {source.Url} — 開催日・競馬場・レース番号の特定に失敗しました。");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"保存エラー: {source.Url} — {ex.Message}");
            }
        }

        return (savedRaceIds, errors);
    }

    /// <summary>
    /// URL 発見 → スクレイプ → DB 保存の全ステップを実行する。
    /// </summary>
    /// <param name="raceDate">収集対象の開催日付</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>収集結果</returns>
    public async Task<JraRaceResultCollectionResult> CollectAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("JRA race result collection started. RaceDate={RaceDate}", raceDate);

        // Step 1: ブラウザ操作による URL 発見
        var discoveredUrls = await DiscoverUrlsAsync(raceDate, cancellationToken);
        var targetUrls = await FilterUnregisteredUrlsAsync(discoveredUrls, raceDate, cancellationToken);
        _logger.LogInformation(
            "JRA race result collection discovery done. RaceDate={RaceDate} DiscoveredCount={DiscoveredCount} TargetCount={TargetCount}",
            raceDate,
            discoveredUrls.Count,
            targetUrls.Count);

        // Step 2: 決定的なスクレイピング
        var scraped = await ScrapeAllAsync(targetUrls, cancellationToken);
        _logger.LogInformation(
            "JRA race result collection scraping done. RaceDate={RaceDate} ScrapedCount={ScrapedCount}",
            raceDate,
            scraped.Count);

        // Step 3: DB 保存（EventFlow コマンド経由）
        var (savedRaceIds, errors) = await SaveAllAsync(scraped, cancellationToken);
        _logger.LogInformation(
            "JRA race result collection save done. RaceDate={RaceDate} SavedCount={SavedCount} ErrorCount={ErrorCount}",
            raceDate,
            savedRaceIds.Count,
            errors.Count);

        return new JraRaceResultCollectionResult(
            RaceDate: raceDate,
            DiscoveredUrls: targetUrls,
            ScrapedResults: scraped.Select(s => s.Data).ToList(),
            SavedRaceIds: savedRaceIds,
            Errors: errors);
    }

    public async Task<JraRaceResultCollectionResult> CollectRaceAsync(
        DateOnly raceDate,
        string racecourseCode,
        int raceNumber,
        CancellationToken cancellationToken = default)
    {
        var discoveredUrls = await DiscoverUrlsAsync(raceDate, cancellationToken).ConfigureAwait(false);
        var targetUrl = discoveredUrls.FirstOrDefault(url => IsTargetRace(url, raceDate, racecourseCode, raceNumber));

        if (targetUrl is null)
        {
            return new JraRaceResultCollectionResult(
                RaceDate: raceDate,
                DiscoveredUrls: discoveredUrls,
                ScrapedResults: [],
                SavedRaceIds: [],
                Errors: [$"対象レースの成績 URL を特定できませんでした。RaceDate={raceDate:yyyy-MM-dd} RacecourseCode={racecourseCode} RaceNumber={raceNumber}"]);
        }

        var data = await _scraper.ScrapeAsync(targetUrl.Url, cancellationToken).ConfigureAwait(false);
        if (data is null)
        {
            return new JraRaceResultCollectionResult(
                RaceDate: raceDate,
                DiscoveredUrls: discoveredUrls,
                ScrapedResults: [],
                SavedRaceIds: [],
                Errors: [$"対象レースの成績スクレイプに失敗しました。Url={targetUrl.Url}"]);
        }

        var (savedRaceIds, errors) = await SaveAllAsync([(targetUrl, data)], cancellationToken).ConfigureAwait(false);

        return new JraRaceResultCollectionResult(
            RaceDate: raceDate,
            DiscoveredUrls: [targetUrl],
            ScrapedResults: [data],
            SavedRaceIds: savedRaceIds,
            Errors: errors);
    }

    private static string? BuildRaceKey(RaceSearchSummary summary)
    {
        if (summary.RaceDate is null || summary.RaceNumber is null || string.IsNullOrWhiteSpace(summary.RacecourseCode))
            return null;

        var racecourseKey = NormalizeRacecourseKey(summary.RacecourseCode);
        return racecourseKey is null
            ? null
            : $"{summary.RaceDate:yyyy-MM-dd}|{racecourseKey}|{summary.RaceNumber.Value:D2}";
    }

    private static string? BuildRaceKey(JraRaceResultUrl url)
    {
        if (url.RaceDate is null || url.RaceNumber is null)
            return null;

        var racecourse = !string.IsNullOrWhiteSpace(url.Racecourse)
            ? url.Racecourse
            : url.RacecourseCode;

        if (string.IsNullOrWhiteSpace(racecourse))
            return null;

        var racecourseKey = NormalizeRacecourseKey(racecourse);
        return racecourseKey is null
            ? null
            : $"{url.RaceDate:yyyy-MM-dd}|{racecourseKey}|{url.RaceNumber.Value:D2}";
    }

    private static string? NormalizeRacecourseKey(string? racecourse)
    {
        if (string.IsNullOrWhiteSpace(racecourse))
            return null;

        var normalized = DeterministicIdGenerator.NormalizeKey(racecourse);
        if (RacecourseAliases.TryGetValue(racecourse.Trim(), out var canonical)
            || RacecourseAliases.TryGetValue(normalized, out canonical))
        {
            return DeterministicIdGenerator.NormalizeKey(canonical);
        }

        return normalized;
    }

    private static bool IsTargetRace(JraRaceResultUrl url, DateOnly raceDate, string racecourseCode, int raceNumber)
    {
        if (url.RaceDate != raceDate || url.RaceNumber != raceNumber)
        {
            return false;
        }

        var targetRaceKey = NormalizeRacecourseKey(racecourseCode);
        var urlRaceKey = NormalizeRacecourseKey(url.Racecourse ?? url.RacecourseCode);

        return !string.IsNullOrWhiteSpace(targetRaceKey)
            && string.Equals(targetRaceKey, urlRaceKey, StringComparison.Ordinal);
    }

    private static bool HasRequiredResultMetadata(RacePredictionContextReadModel? context)
    {
        return context is not null
            && !string.IsNullOrWhiteSpace(context.SurfaceCode)
            && context.DistanceMeters is > 0
            && !string.IsNullOrWhiteSpace(context.GradeCode)
            && context.Status >= RaceStatus.ResultDeclared;
    }

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private async Task<string?> TrySaveResultAsync(
        JraRaceResultUrl source,
        JraRaceResultData data,
        CancellationToken cancellationToken)
    {
        data = await EnrichCourseInformationFromRaceCardAsync(source, data, cancellationToken).ConfigureAwait(false);

        // 開催日: スクレイプ結果優先、フォールバックは URL から解析した値
        var raceDate = data.RaceDate ?? source.RaceDate;
        // レース番号: スクレイプ結果優先
        var raceNumber = data.RaceNumber ?? source.RaceNumber;
        // 競馬場: スクレイプ結果の日本語名優先、フォールバックは URL の数値コード
        var racecourse = !string.IsNullOrWhiteSpace(data.Racecourse)
            ? data.Racecourse
            : source.Racecourse ?? source.RacecourseCode;

        if (raceDate is null || raceNumber is null || racecourse is null)
        {
            return null;
        }

        var raceName = string.IsNullOrWhiteSpace(data.RaceName)
            ? $"R{raceNumber}"
            : data.RaceName;

        ValidateRaceResultCourseInformation(data, source.Url);

        // レースを Upsert（存在しない場合は作成）
        var raceId = await _writeTools.UpsertRace(
            raceDate: raceDate.Value.ToString("yyyy-MM-dd"),
            racecourseCode: racecourse,
            raceNumber: raceNumber.Value,
            raceName: raceName,
            entryCount: data.Entries.Count > 0 ? data.Entries.Count : null,
            gradeCode: data.Grade,
            surfaceCode: data.CourseType,
            distanceMeters: data.Distance,
            directionCode: data.Direction,
            cancellationToken: cancellationToken);

        foreach (var entry in data.Entries)
        {
            if (entry.HorseNumber <= 0)
            {
                _logger.LogWarning(
                    "Skip race-entry registration because horse number is missing. RaceId={RaceId} Url={Url} HorseName={HorseName}",
                    raceId,
                    source.Url,
                    entry.HorseName);
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.HorseName) || string.IsNullOrWhiteSpace(entry.TrainerName))
            {
                throw new InvalidOperationException(
                    $"出走登録バリデーションエラー: raceId={raceId}, horseNumber={entry.HorseNumber}, horseName='{entry.HorseName}', trainerName='{entry.TrainerName}'");
            }

            var (sexCode, age) = ParseSexAge(entry.SexAge);
            await _writeTools.UpsertRaceEntry(
                raceId: raceId,
                horseNumber: entry.HorseNumber,
                horseName: entry.HorseName,
                jockeyName: entry.JockeyName,
                trainerName: entry.TrainerName,
                gateNumber: entry.GateNumber,
                assignedWeight: entry.Weight,
                sexCode: sexCode,
                age: age,
                declaredWeight: entry.BodyWeight,
                declaredWeightDiff: entry.BodyWeightDiff,
                cancellationToken: cancellationToken);
        }

        // 勝ち馬を特定して結果を宣言
        var winner = data.Entries.FirstOrDefault(e => e.FinishPosition == 1);
        if (winner is null || string.IsNullOrWhiteSpace(winner.HorseName))
        {
            throw new InvalidOperationException(
                $"結果登録バリデーションエラー: 勝ち馬を特定できません。raceId={raceId}, raceName='{data.RaceName}', sourceUrl='{source.Url}'");
        }

        await _writeTools.DeclareRaceResult(
            raceId: raceId,
            winningHorseName: winner.HorseName,
            cancellationToken: cancellationToken);

        // 各馬の成績を記録
        foreach (var entry in data.Entries)
        {
            if (entry.HorseNumber <= 0)
            {
                _logger.LogWarning(
                    "Skip entry-result registration because horse number is missing. RaceId={RaceId} Url={Url} HorseName={HorseName}",
                    raceId,
                    source.Url,
                    entry.HorseName);
                continue;
            }

            await _writeTools.DeclareRaceEntryResult(
                raceId: raceId,
                horseNumber: entry.HorseNumber,
                finishPosition: entry.FinishPosition,
                officialTime: entry.OfficialTime,
                marginText: entry.MarginText,
                lastThreeFurlongTime: entry.LastThreeFurlongTime,
                abnormalResultCode: entry.AbnormalResultCode,
                cancellationToken: cancellationToken);
        }

        // 払い戻しデータを記録
        if (data.Payouts is not null)
        {
            await SavePayoutsAsync(raceId, data.Payouts, cancellationToken);
        }

        return raceId;
    }

    private static (string? SexCode, int? Age) ParseSexAge(string? sexAge)
    {
        return JraSexAgeParser.Parse(sexAge);
    }

    private static void ValidateRaceResultCourseInformation(JraRaceResultData data, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(data.CourseType)
            || data.Distance is null or <= 0
            || string.IsNullOrWhiteSpace(data.Grade))
        {
            throw new InvalidOperationException(
                $"結果コース情報バリデーションエラー: raceName='{data.RaceName}', courseType='{data.CourseType}', distance='{data.Distance}', grade='{data.Grade}', sourceUrl='{sourceUrl}'");
        }
    }

    private async Task<JraRaceResultData> EnrichCourseInformationFromRaceCardAsync(
        JraRaceResultUrl source,
        JraRaceResultData data,
        CancellationToken cancellationToken)
    {
        if (_raceCardScraper is null || HasRequiredCourseInformation(data))
        {
            return data;
        }

        try
        {
            var opened = await _scraper
                .TryOpenRaceCardFromResultPageAsync(source.Url, cancellationToken)
                .ConfigureAwait(false);
            if (!opened)
            {
                return data;
            }

            var raceCard = await _raceCardScraper.ScrapeCurrentPageAsync(cancellationToken).ConfigureAwait(false);
            if (raceCard is null)
            {
                return data;
            }

            return data with
            {
                CourseType = string.IsNullOrWhiteSpace(data.CourseType) ? raceCard.CourseType : data.CourseType,
                Distance = data.Distance is > 0 ? data.Distance : raceCard.Distance,
                Direction = string.IsNullOrWhiteSpace(data.Direction) ? raceCard.TrackDirection : data.Direction,
                Grade = string.IsNullOrWhiteSpace(data.Grade) ? raceCard.Grade : data.Grade,
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Race result course metadata fallback from race card failed. ResultUrl={ResultUrl}",
                data.Url);
            return data;
        }
    }

    private static bool HasRequiredCourseInformation(JraRaceResultData data)
    {
        return !string.IsNullOrWhiteSpace(data.CourseType)
            && data.Distance is > 0
            && !string.IsNullOrWhiteSpace(data.Grade)
            && !string.IsNullOrWhiteSpace(data.Direction);
    }

    private async Task SavePayoutsAsync(
        string raceId,
        JraRacePayoutData payouts,
        CancellationToken cancellationToken)
    {
        static string? ToJson(IReadOnlyList<JraPayoutEntry> entries)
        {
            if (entries.Count == 0)
            {
                return null;
            }

            var dtos = entries.Select(e => new { combination = e.Combination, amount = e.Amount });
            return System.Text.Json.JsonSerializer.Serialize(dtos);
        }

        await _writeTools.DeclareRacePayouts(
            raceId: raceId,
            winPayoutsJson: ToJson(payouts.WinPayouts),
            placePayoutsJson: ToJson(payouts.PlacePayouts),
            quinellaPayoutsJson: ToJson(payouts.QuinellaPayouts),
            exactaPayoutsJson: ToJson(payouts.ExactaPayouts),
            trifectaPayoutsJson: ToJson(payouts.TrifectaPayouts),
            cancellationToken: cancellationToken);
    }
}
