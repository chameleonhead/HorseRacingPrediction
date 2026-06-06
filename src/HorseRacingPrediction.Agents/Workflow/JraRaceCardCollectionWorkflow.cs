using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Agents.JraAgent;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// JRA 出馬表データを収集して DB へ保存するワークフロー。
/// <para>
/// ワークフロー:
/// <list type="number">
///   <item><see cref="DiscoverUrlsAsync"/> — browser-only discovery が JRA から URL を収集</item>
///   <item><see cref="ScrapeAllAsync"/> — 各 URL を Playwright で決定的にスクレイプ（AI 不使用）</item>
///   <item><see cref="SaveAllAsync"/> — スクレイプ結果を EventFlow コマンド経由で保存（AI 不使用）</item>
///   <item><see cref="CollectAsync"/> — 上記3ステップをまとめて実行</item>
/// </list>
/// </para>
/// </summary>
public sealed class JraRaceCardCollectionWorkflow
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
        };

    private readonly JraRaceCardUrlDiscoveryAgent _discoveryAgent;
    private readonly JraRaceCardScraper _scraper;
    private readonly DataCollectionWriteTools _writeTools;

    internal JraRaceCardCollectionWorkflow(
        JraRaceCardUrlDiscoveryAgent discoveryAgent,
        JraRaceCardScraper scraper,
        DataCollectionWriteTools writeTools)
    {
        _discoveryAgent = discoveryAgent;
        _scraper = scraper;
        _writeTools = writeTools;
    }

    public JraRaceCardCollectionWorkflow(
        IWebBrowser browser,
        JraRaceCardScraper scraper,
        DataCollectionWriteTools writeTools)
        : this(new JraRaceCardUrlDiscoveryAgent(browser), scraper, writeTools)
    {
    }

    /// <summary>
    /// 指定した週末の出馬表 URL 一覧を AI エージェントで発見して返す。
    /// </summary>
    public Task<IReadOnlyList<JraRaceCardUrl>> DiscoverUrlsAsync(
        DateOnly weekendDate,
        CancellationToken cancellationToken = default)
        => _discoveryAgent.DiscoverUrlsAsync(weekendDate, cancellationToken);

    /// <summary>
    /// 指定した URL 一覧を Playwright でスクレイプして出馬表データを返す。
    /// AI は使用しない。スクレイプに失敗した URL は結果から除外される。
    /// </summary>
    public async Task<IReadOnlyList<(JraRaceCardUrl Source, JraRaceCardData Data)>> ScrapeAllAsync(
        IReadOnlyList<JraRaceCardUrl> urls,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(JraRaceCardUrl, JraRaceCardData)>();

        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var data = await _scraper.ScrapeAsync(url.Url, cancellationToken).ConfigureAwait(false);
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
        IReadOnlyList<(JraRaceCardUrl Source, JraRaceCardData Data)> scraped,
        CancellationToken cancellationToken = default)
    {
        var savedRaceIds = new List<string>();
        var errors = new List<string>();

        foreach (var (source, data) in scraped)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var raceId = await TrySaveRaceAsync(source, data, cancellationToken);
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
    /// <param name="weekendDate">収集対象の週末日付（土曜または日曜）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>収集結果</returns>
    public async Task<JraRaceCardCollectionResult> CollectAsync(
        DateOnly weekendDate,
        CancellationToken cancellationToken = default)
    {
        // Step 1: browser-only discovery による URL 発見
        var discoveredUrls = await DiscoverUrlsAsync(weekendDate, cancellationToken);

        // Step 2: 決定的なスクレイピング（AI 不使用）
        var scraped = await ScrapeAllAsync(discoveredUrls, cancellationToken);

        // Step 3: DB 保存（AI 不使用、EventFlow コマンド経由）
        var (savedRaceIds, errors) = await SaveAllAsync(scraped, cancellationToken);

        return new JraRaceCardCollectionResult(
            WeekendDate: weekendDate,
            DiscoveredUrls: discoveredUrls,
            ScrapedCards: scraped.Select(s => s.Data).ToList(),
            SavedRaceIds: savedRaceIds,
            Errors: errors);
    }

    public async Task<JraRaceCardCollectionResult> CollectRaceAsync(
        DateOnly raceDate,
        string racecourseCode,
        int raceNumber,
        CancellationToken cancellationToken = default)
    {
        var discoveredUrls = await DiscoverUrlsAsync(raceDate, cancellationToken).ConfigureAwait(false);
        var targetUrl = discoveredUrls.FirstOrDefault(url => IsTargetRace(url, raceDate, racecourseCode, raceNumber));

        if (targetUrl is null)
        {
            return new JraRaceCardCollectionResult(
                WeekendDate: raceDate,
                DiscoveredUrls: discoveredUrls,
                ScrapedCards: [],
                SavedRaceIds: [],
                Errors: [$"対象レースの出馬表 URL を特定できませんでした。RaceDate={raceDate:yyyy-MM-dd} RacecourseCode={racecourseCode} RaceNumber={raceNumber}"]);
        }

        var data = await _scraper.ScrapeAsync(targetUrl.Url, cancellationToken).ConfigureAwait(false);
        if (data is null)
        {
            return new JraRaceCardCollectionResult(
                WeekendDate: raceDate,
                DiscoveredUrls: discoveredUrls,
                ScrapedCards: [],
                SavedRaceIds: [],
                Errors: [$"対象レースの出馬表スクレイプに失敗しました。Url={targetUrl.Url}"]);
        }

        var (savedRaceIds, errors) = await SaveAllAsync([(targetUrl, data)], cancellationToken).ConfigureAwait(false);

        return new JraRaceCardCollectionResult(
            WeekendDate: raceDate,
            DiscoveredUrls: [targetUrl],
            ScrapedCards: [data],
            SavedRaceIds: savedRaceIds,
            Errors: errors);
    }

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private static bool IsTargetRace(JraRaceCardUrl url, DateOnly raceDate, string racecourseCode, int raceNumber)
    {
        if (url.RaceDate != raceDate || url.RaceNumber != raceNumber)
        {
            return false;
        }

        var targetRacecourse = NormalizeRacecourseKey(racecourseCode);
        var urlRacecourse = NormalizeRacecourseKey(url.Racecourse ?? url.RacecourseCode);

        return !string.IsNullOrWhiteSpace(targetRacecourse)
            && string.Equals(targetRacecourse, urlRacecourse, StringComparison.Ordinal);
    }

    private static string? NormalizeRacecourseKey(string? racecourse)
    {
        if (string.IsNullOrWhiteSpace(racecourse))
        {
            return null;
        }

        var normalized = DeterministicIdGenerator.NormalizeKey(racecourse);
        if (RacecourseAliases.TryGetValue(racecourse.Trim(), out var canonical)
            || RacecourseAliases.TryGetValue(normalized, out canonical))
        {
            return DeterministicIdGenerator.NormalizeKey(canonical);
        }

        return normalized;
    }

    private async Task<string?> TrySaveRaceAsync(
        JraRaceCardUrl source,
        JraRaceCardData data,
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

        ValidateRaceCardCourseInformation(data, source.Url);

        var raceId = await _writeTools.UpsertRace(
            raceDate: raceDate.Value.ToString("yyyy-MM-dd"),
            racecourseCode: racecourse,
            raceNumber: raceNumber.Value,
            raceName: raceName,
            entryCount: data.Entries.Count > 0 ? data.Entries.Count : null,
            gradeCode: data.Grade,
            surfaceCode: data.CourseType,
            distanceMeters: data.Distance,
            directionCode: data.TrackDirection,
            cancellationToken: cancellationToken);

        foreach (var entry in data.Entries)
        {
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

        return raceId;
    }
    private static bool TryResolveRacecourseDisplayName(JraRaceCardUrl source, out string racecourse)
    {
        if (!string.IsNullOrWhiteSpace(source.RacecourseCode)
            && RacecourseAliases.TryGetValue(source.RacecourseCode, out var displayName))
        {
            racecourse = displayName;
            return true;
        }

        racecourse = source.Racecourse ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(racecourse))
        {
            return true;
        }

        racecourse = string.Empty;
        return false;
    }

    private static (string? sexCode, int? age) ParseSexAge(string? sexAge)
    {
        var (sexCode, age) = JraSexAgeParser.Parse(sexAge);
        return (sexCode, age);
    }

    private static void ValidateRaceCardCourseInformation(JraRaceCardData data, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(data.CourseType) || data.Distance is null or <= 0)
        {
            throw new InvalidOperationException(
                $"出馬表コース情報バリデーションエラー: raceName='{data.RaceName}', courseType='{data.CourseType}', distance='{data.Distance}', sourceUrl='{sourceUrl}'");
        }
    }
}
