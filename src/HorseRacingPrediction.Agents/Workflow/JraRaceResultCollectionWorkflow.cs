using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// JRA 成績データを収集して DB へ保存するワークフロー。
/// <para>
/// AI（<see cref="JraRaceResultUrlDiscoveryAgent"/>）は成績 URL の「発見」のみを担当し、
/// 各ページの詳細スクレイピングは Playwright ベースの <see cref="JraRaceResultScraper"/> が行う。
/// 最後に <see cref="DataCollectionWriteTools"/> で EventFlow 経由でドメインへ保存する。
/// </para>
/// <para>
/// ワークフロー:
/// <list type="number">
///   <item><see cref="DiscoverUrlsAsync"/> — AI エージェントが JRA 成績一覧から URL を収集</item>
///   <item><see cref="ScrapeAllAsync"/> — 各 URL を Playwright で決定的にスクレイプ（AI 不使用）</item>
///   <item><see cref="SaveAllAsync"/> — スクレイプ結果を EventFlow コマンド経由で保存（AI 不使用）</item>
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

    private readonly JraRaceResultUrlDiscoveryAgent _discoveryAgent;
    private readonly JraRaceResultScraper _scraper;
    private readonly DataCollectionWriteTools _writeTools;
    private readonly IRaceQueryService? _queryService;
    private readonly ILogger<JraRaceResultCollectionWorkflow> _logger;

    internal JraRaceResultCollectionWorkflow(
        JraRaceResultUrlDiscoveryAgent discoveryAgent,
        JraRaceResultScraper scraper,
        DataCollectionWriteTools writeTools,
        IRaceQueryService? queryService = null,
        ILogger<JraRaceResultCollectionWorkflow>? logger = null)
    {
        _discoveryAgent = discoveryAgent;
        _scraper = scraper;
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
            new JraRaceResultUrlDiscoveryAgent(
                browser,
                loggerFactory?.CreateLogger<JraRaceResultUrlDiscoveryAgent>()),
            scraper,
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
            new JraRaceResultUrlDiscoveryAgent(
                browser,
                loggerFactory?.CreateLogger<JraRaceResultUrlDiscoveryAgent>()),
            scraper,
            writeTools,
            queryService,
            logger)
    {
    }

    /// <summary>
    /// 指定した開催日の成績 URL 一覧を AI エージェントで発見して返す。
    /// </summary>
    public Task<IReadOnlyList<JraRaceResultUrl>> DiscoverUrlsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken = default)
        => _discoveryAgent.DiscoverUrlsAsync(raceDate, cancellationToken);

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

        var registeredKeys = registeredRaces
            .Select(BuildRaceKey)
            .Where(x => x is not null)
            .ToHashSet(StringComparer.Ordinal);

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var filtered = new List<JraRaceResultUrl>(urls.Count);

        foreach (var url in urls)
        {
            var key = BuildRaceKey(url);
            if (key is not null)
            {
                if (registeredKeys.Contains(key) || !seenKeys.Add(key))
                    continue;
            }

            filtered.Add(url);
        }

        return filtered;
    }

    /// <summary>
    /// 指定した URL 一覧を Playwright でスクレイプして成績データを返す。
    /// AI は使用しない。スクレイプに失敗した URL は結果から除外される。
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
    /// AI は使用しない。保存に失敗したエントリはエラーメッセージとして返される。
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

        // Step 1: AI による URL 発見（少ないページ閲覧でトークン節約）
        var discoveredUrls = await DiscoverUrlsAsync(raceDate, cancellationToken);
        var targetUrls = await FilterUnregisteredUrlsAsync(discoveredUrls, raceDate, cancellationToken);
        _logger.LogInformation(
            "JRA race result collection discovery done. RaceDate={RaceDate} DiscoveredCount={DiscoveredCount} TargetCount={TargetCount}",
            raceDate,
            discoveredUrls.Count,
            targetUrls.Count);

        // Step 2: 決定的なスクレイピング（AI 不使用）
        var scraped = await ScrapeAllAsync(targetUrls, cancellationToken);
        _logger.LogInformation(
            "JRA race result collection scraping done. RaceDate={RaceDate} ScrapedCount={ScrapedCount}",
            raceDate,
            scraped.Count);

        // Step 3: DB 保存（AI 不使用、EventFlow コマンド経由）
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

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private async Task<string?> TrySaveResultAsync(
        JraRaceResultUrl source,
        JraRaceResultData data,
        CancellationToken cancellationToken)
    {
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
        if (winner is not null && !string.IsNullOrWhiteSpace(winner.HorseName))
        {
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
        }

        return raceId;
    }

    private static (string? SexCode, int? Age) ParseSexAge(string? sexAge)
    {
        if (string.IsNullOrWhiteSpace(sexAge))
        {
            return (null, null);
        }

        var normalized = sexAge.Trim();
        var sexCode = normalized[0] switch
        {
            '牡' => "M",
            '牝' => "F",
            'セ' => "G",
            _ => null
        };

        var ageDigits = new string(normalized.Skip(1).Where(char.IsDigit).ToArray());
        int? age = int.TryParse(ageDigits, out var parsedAge) ? parsedAge : null;
        return (sexCode, age);
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
