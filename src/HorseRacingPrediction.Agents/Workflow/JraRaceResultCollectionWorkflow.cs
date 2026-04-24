using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Agents.Scrapers.Jra;

namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// JRA 成績データを収集して DB へ保存するワークフロー。
/// <para>
/// AI（<see cref="JraResultUrlDiscoveryAgent"/>）は成績 URL の「発見」のみを担当し、
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
    private readonly JraResultUrlDiscoveryAgent _discoveryAgent;
    private readonly JraRaceResultScraper _scraper;
    private readonly DataCollectionWriteTools _writeTools;

    public JraRaceResultCollectionWorkflow(
        JraResultUrlDiscoveryAgent discoveryAgent,
        JraRaceResultScraper scraper,
        DataCollectionWriteTools writeTools)
    {
        _discoveryAgent = discoveryAgent;
        _scraper = scraper;
        _writeTools = writeTools;
    }

    /// <summary>
    /// 指定した開催日の成績 URL 一覧を AI エージェントで発見して返す。
    /// </summary>
    public Task<IReadOnlyList<JraRaceResultUrl>> DiscoverUrlsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken = default)
        => _discoveryAgent.DiscoverUrlsAsync(raceDate, cancellationToken);

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
        // Step 1: AI による URL 発見（少ないページ閲覧でトークン節約）
        var discoveredUrls = await DiscoverUrlsAsync(raceDate, cancellationToken);

        // Step 2: 決定的なスクレイピング（AI 不使用）
        var scraped = await ScrapeAllAsync(discoveredUrls, cancellationToken);

        // Step 3: DB 保存（AI 不使用、EventFlow コマンド経由）
        var (savedRaceIds, errors) = await SaveAllAsync(scraped, cancellationToken);

        return new JraRaceResultCollectionResult(
            RaceDate: raceDate,
            DiscoveredUrls: discoveredUrls,
            ScrapedResults: scraped.Select(s => s.Data).ToList(),
            SavedRaceIds: savedRaceIds,
            Errors: errors);
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

        // 勝ち馬を特定して結果を宣言
        var winner = data.Entries.FirstOrDefault(e => e.FinishPosition == 1);
        if (winner is not null)
        {
            await _writeTools.DeclareRaceResult(
                raceId: raceId,
                winningHorseName: winner.HorseName,
                cancellationToken: cancellationToken);

            // 各馬の成績を記録
            foreach (var entry in data.Entries)
            {
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
