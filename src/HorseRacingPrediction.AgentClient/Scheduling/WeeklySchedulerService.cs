using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Scheduling;

/// <summary>
/// 任意のレース開催日を起点とした競馬予測サイクルを自律的に実行するバックグラウンドサービス。
/// <para>
/// <see cref="WeeklySchedulerOptions"/> の <see cref="WeeklySchedulerOptions.FirstRaceDayOfWeek"/>
/// で設定した曜日を「第1開催日」とし、その相対日数に基づいて以下のフェーズを自動実行する。
/// 土日開催（デフォルト）のほか、中山金杯のような任意曜日の開催にも対応できる。
/// </para>
/// <list type="number">
///   <item>
///     <b>第1開催日 -2 日 朝</b>: <c>JraRaceCardCollectionWorkflow.CollectAsync()</c> で出馬表 URL を発見・保存。
///     続いて <c>WeeklyScheduleWorkflow.DiscoverRacesAsync()</c> で AI がレース一覧を構築し、
///     <c>CollectDataAsync()</c> で馬・騎手・厩舎データを一括収集する。
///   </item>
///   <item>
///     <b>第1開催日 -2 日 昼</b>: データ再収集（データ更新のため）。
///   </item>
///   <item>
///     <b>第1開催日 -1 日 朝</b>: <c>JraRaceCardCollectionWorkflow.CollectAsync()</c> で出馬表を再取得。
///   </item>
///   <item>
///     <b>第1開催日 -1 日 夕</b>: <c>WeeklyScheduleWorkflow.CollectPostPositionsAndPredictAsync()</c> で
///     枠順確定後のデータを収集し、AI が予測レポートを作成する。
///   </item>
///   <item>
///     <b>第1開催日 朝</b>: <c>JraRaceCardCollectionWorkflow.CollectAsync()</c> で当日出馬表を更新。
///   </item>
///   <item>
///     <b>第1開催日 夜</b>: <c>JraRaceResultCollectionWorkflow.CollectAsync()</c> で当日成績を収集。
///   </item>
///   <item>
///     <b>第2開催日（第1開催日翌日）朝</b>: 同様に当日出馬表を更新。
///   </item>
///   <item>
///     <b>第2開催日 夜</b>: 当日成績を収集。
///   </item>
///   <item>
///     <b>第1開催日 +2 日 朝</b>: 最終成績を収集してサイクルを完結。
///   </item>
/// </list>
/// <para>
/// 発見したレース情報は <see cref="WeeklyStateStore"/> に JSON ファイルとして保存され、
/// アプリ再起動後も後続フェーズで再利用できる。
/// </para>
/// </summary>
public sealed class WeeklySchedulerService : BackgroundService
{
    // JST は UTC+9
    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");

    private readonly WeeklyScheduleWorkflow _weeklyWorkflow;
    private readonly JraRaceCardCollectionWorkflow _raceCardWorkflow;
    private readonly JraRaceResultCollectionWorkflow _raceResultWorkflow;
    private readonly WeeklyStateStore _stateStore;
    private readonly WeeklySchedulerOptions _options;
    private readonly ILogger<WeeklySchedulerService> _logger;

    public WeeklySchedulerService(
        WeeklyScheduleWorkflow weeklyWorkflow,
        JraRaceCardCollectionWorkflow raceCardWorkflow,
        JraRaceResultCollectionWorkflow raceResultWorkflow,
        WeeklyStateStore stateStore,
        IOptions<WeeklySchedulerOptions> options,
        ILogger<WeeklySchedulerService> logger)
    {
        _weeklyWorkflow = weeklyWorkflow;
        _raceCardWorkflow = raceCardWorkflow;
        _raceResultWorkflow = raceResultWorkflow;
        _stateStore = stateStore;
        _options = options.Value;
        _logger = logger;
    }

    // ------------------------------------------------------------------ //
    // BackgroundService
    // ------------------------------------------------------------------ //

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("WeeklySchedulerService は無効化されています（Enabled = false）。");
            return;
        }

        _logger.LogInformation("WeeklySchedulerService を開始しました。");

        while (!stoppingToken.IsCancellationRequested)
        {
            var (nextTime, phase, firstRaceDay) = GetNextScheduledItemWithFirstRaceDay(NowJst());
            var delay = nextTime - NowJst();

            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation(
                    "次のフェーズ [{Phase}] まで待機します: {NextTime:yyyy-MM-dd HH:mm} JST (約 {Minutes} 分後)",
                    phase, nextTime, (int)delay.TotalMinutes);

                try
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            await ExecutePhaseAsync(phase, firstRaceDay, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("WeeklySchedulerService を停止しました。");
    }

    // ------------------------------------------------------------------ //
    // Phase execution
    // ------------------------------------------------------------------ //

    private async Task ExecutePhaseAsync(
        SchedulePhase phase,
        DateOnly firstRaceDay,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[{Phase}] フェーズを開始します (第1開催日: {FirstRaceDay})", phase, firstRaceDay);

        try
        {
            switch (phase)
            {
                case SchedulePhase.Discovery:
                    await RunDiscoveryAsync(firstRaceDay, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.DataRefresh:
                    await RunDataRefreshAsync(firstRaceDay, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.PreRaceCard:
                    await RunRaceCardCollectionAsync(firstRaceDay, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.PostPosition:
                    await RunPostPositionPredictionAsync(firstRaceDay, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.RaceDay1Card:
                    await RunRaceCardCollectionAsync(firstRaceDay, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.RaceDay1Results:
                    await RunResultCollectionAsync(firstRaceDay, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.RaceDay2Card:
                    await RunRaceCardCollectionAsync(GetRaceDay2(firstRaceDay), cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.RaceDay2Results:
                    await RunResultCollectionAsync(GetRaceDay2(firstRaceDay), cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.FinalResults:
                    // 第1・第2開催日両日の成績を収集（最終確定分）
                    await RunResultCollectionAsync(firstRaceDay, cancellationToken).ConfigureAwait(false);
                    await RunResultCollectionAsync(GetRaceDay2(firstRaceDay), cancellationToken).ConfigureAwait(false);
                    _stateStore.DeleteState(firstRaceDay);
                    break;
            }

            _logger.LogInformation("[{Phase}] フェーズが正常に完了しました。", phase);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[{Phase}] フェーズがキャンセルされました。", phase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Phase}] フェーズ実行中にエラーが発生しました。", phase);
        }
    }

    // ------------------------------------------------------------------ //
    // Individual phase implementations
    // ------------------------------------------------------------------ //

    /// <summary>発見フェーズ: AI によるレース発見 + 初回データ収集</summary>
    private async Task RunDiscoveryAsync(DateOnly firstRaceDay, CancellationToken ct)
    {
        // JRA 出馬表 URL の発見・スクレイプ・DB 保存
        _logger.LogInformation("[発見] JRA 出馬表収集を開始します (第1開催日: {FirstRaceDay})", firstRaceDay);
        var raceCardResult = await _raceCardWorkflow.CollectAsync(firstRaceDay, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "[発見] 出馬表収集完了: {SavedCount} レース保存 (エラー {ErrorCount} 件)",
            raceCardResult.SavedRaceIds.Count, raceCardResult.Errors.Count);

        // AI によるレース発見
        _logger.LogInformation("[発見] AI によるレース発見を開始します。");
        var races = await _weeklyWorkflow.DiscoverRacesAsync(firstRaceDay, ct).ConfigureAwait(false);
        _logger.LogInformation("[発見] {Count} レースを発見しました。", races.Count);

        if (races.Count == 0)
        {
            _logger.LogWarning("[発見] レースが発見されませんでした。データ収集をスキップします。");
            return;
        }

        // 状態を保存（後続フェーズで再利用）
        await _stateStore.SaveRacesAsync(firstRaceDay, races, ct).ConfigureAwait(false);

        // 初回データ収集
        _logger.LogInformation("[発見] 初回データ収集を開始します ({Count} レース)。", races.Count);
        var collectionResult = await _weeklyWorkflow.CollectDataAsync(firstRaceDay, races, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "[発見] データ収集完了: {Count} レース分のデータを収集しました。",
            collectionResult.RaceCollections.Count);
    }

    /// <summary>データ再収集</summary>
    private async Task RunDataRefreshAsync(DateOnly firstRaceDay, CancellationToken ct)
    {
        var races = await _stateStore.LoadRacesAsync(firstRaceDay, ct).ConfigureAwait(false);

        if (races is null || races.Count == 0)
        {
            _logger.LogWarning("保存済みレース情報がありません。レース発見から再実行します。");
            await RunDiscoveryAsync(firstRaceDay, ct).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation("[データ再収集] {Count} レースのデータを再収集します。", races.Count);
        var result = await _weeklyWorkflow.CollectDataAsync(firstRaceDay, races, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "[データ再収集] 完了: {Count} レース分のデータを更新しました。",
            result.RaceCollections.Count);
    }

    /// <summary>JRA 出馬表収集</summary>
    private async Task RunRaceCardCollectionAsync(DateOnly targetDate, CancellationToken ct)
    {
        _logger.LogInformation("[出馬表収集] JRA 出馬表収集を開始します (日付: {Date})", targetDate);
        var result = await _raceCardWorkflow.CollectAsync(targetDate, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "[出馬表収集] 完了: {SavedCount} レース保存 (エラー {ErrorCount} 件)",
            result.SavedRaceIds.Count, result.Errors.Count);

        foreach (var error in result.Errors)
        {
            _logger.LogWarning("[出馬表収集] エラー: {Error}", error);
        }
    }

    /// <summary>前日夕方フェーズ: 枠順確定後データ収集 + AI 予測</summary>
    private async Task RunPostPositionPredictionAsync(DateOnly firstRaceDay, CancellationToken ct)
    {
        var races = await _stateStore.LoadRacesAsync(firstRaceDay, ct).ConfigureAwait(false);

        if (races is null || races.Count == 0)
        {
            _logger.LogWarning(
                "[前日予測] 保存済みレース情報がありません。発見フェーズを先に実行してください。");
            return;
        }

        _logger.LogInformation("[前日予測] 枠順確定後データ収集 + 予測を開始します ({Count} レース)。", races.Count);
        var results = await _weeklyWorkflow
            .CollectPostPositionsAndPredictAsync(races, ct)
            .ConfigureAwait(false);

        foreach (var result in results)
        {
            _logger.LogInformation(
                "[前日予測] {RaceName} ({Racecourse}) の予測が完了しました。",
                result.RaceInfo.RaceName, result.RaceInfo.Racecourse);
            _logger.LogDebug("[前日予測] 予測サマリー: {Summary}", result.PredictionSummary);
        }

        _logger.LogInformation("[前日予測] 全 {Count} レースの予測が完了しました。", results.Count);
    }

    /// <summary>成績収集</summary>
    private async Task RunResultCollectionAsync(DateOnly raceDate, CancellationToken ct)
    {
        _logger.LogInformation("[成績収集] JRA 成績収集を開始します (日付: {Date})", raceDate);
        var result = await _raceResultWorkflow.CollectAsync(raceDate, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "[成績収集] 完了: {SavedCount} レース保存 (エラー {ErrorCount} 件)",
            result.SavedRaceIds.Count, result.Errors.Count);

        foreach (var error in result.Errors)
        {
            _logger.LogWarning("[成績収集] エラー: {Error}", error);
        }
    }

    // ------------------------------------------------------------------ //
    // Schedule calculation
    // ------------------------------------------------------------------ //

    /// <summary>
    /// 現在時刻の次に実行すべきフェーズとその予定時刻を返す。
    /// </summary>
    public (DateTimeOffset NextTime, SchedulePhase Phase) GetNextScheduledItem(DateTimeOffset nowJst)
    {
        var (nextTime, phase, _) = GetNextScheduledItemWithFirstRaceDay(nowJst);
        return (nextTime, phase);
    }

    private (DateTimeOffset NextTime, SchedulePhase Phase, DateOnly FirstRaceDay) GetNextScheduledItemWithFirstRaceDay(
        DateTimeOffset nowJst)
    {
        var candidates = BuildScheduleForCurrentAndNextCycle(nowJst);

        // 現在時刻より後の最初のスケジュールを返す
        var next = candidates
            .Where(c => c.Time > nowJst)
            .OrderBy(c => c.Time)
            .First();

        return (next.Time, next.Phase, next.FirstRaceDay);
    }

    private IEnumerable<(DateTimeOffset Time, SchedulePhase Phase, DateOnly FirstRaceDay)> BuildScheduleForCurrentAndNextCycle(
        DateTimeOffset nowJst)
    {
        var today = DateOnly.FromDateTime(nowJst.DateTime);
        var targetDayOfWeek = (DayOfWeek)_options.FirstRaceDayOfWeek;

        // 次または当日の第1開催日（当日が第1開催日ならば当日）
        var daysUntilFirstRaceDay = ((int)targetDayOfWeek - (int)today.DayOfWeek + 7) % 7;
        var upcomingOrCurrentFirstRaceDay = today.AddDays(daysUntilFirstRaceDay);

        // 前週・今週・来週の 3 サイクル分を生成することで、
        // 開催日直後においても前週の残スケジュール（FinalResults等）が正しく返される。
        foreach (var item in BuildCycleSchedule(upcomingOrCurrentFirstRaceDay.AddDays(-7)))
            yield return item;
        foreach (var item in BuildCycleSchedule(upcomingOrCurrentFirstRaceDay))
            yield return item;
        foreach (var item in BuildCycleSchedule(upcomingOrCurrentFirstRaceDay.AddDays(7)))
            yield return item;
    }

    private IEnumerable<(DateTimeOffset Time, SchedulePhase Phase, DateOnly FirstRaceDay)> BuildCycleSchedule(DateOnly firstRaceDay)
    {
        var discoveryDay = firstRaceDay.AddDays(-2); // 開催日の2日前（発見・データ収集）
        var preRaceDay = firstRaceDay.AddDays(-1);   // 開催前日（出馬表収集・枠順予測）
        var raceDay2 = firstRaceDay.AddDays(1);      // 第2開催日
        var finalDay = firstRaceDay.AddDays(2);      // 最終成績収集日

        yield return (ToJst(discoveryDay, _options.DiscoveryHour), SchedulePhase.Discovery, firstRaceDay);
        yield return (ToJst(discoveryDay, _options.DataRefreshHour), SchedulePhase.DataRefresh, firstRaceDay);
        yield return (ToJst(preRaceDay, _options.PreRaceCardHour), SchedulePhase.PreRaceCard, firstRaceDay);
        yield return (ToJst(preRaceDay, _options.PostPositionHour), SchedulePhase.PostPosition, firstRaceDay);
        yield return (ToJst(firstRaceDay, _options.RaceDay1CardHour), SchedulePhase.RaceDay1Card, firstRaceDay);
        yield return (ToJst(firstRaceDay, _options.RaceDay1ResultsHour), SchedulePhase.RaceDay1Results, firstRaceDay);
        yield return (ToJst(raceDay2, _options.RaceDay2CardHour), SchedulePhase.RaceDay2Card, firstRaceDay);
        yield return (ToJst(raceDay2, _options.RaceDay2ResultsHour), SchedulePhase.RaceDay2Results, firstRaceDay);
        yield return (ToJst(finalDay, _options.FinalResultsHour), SchedulePhase.FinalResults, firstRaceDay);
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private DateTimeOffset NowJst() =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Jst);

    private static DateTimeOffset ToJst(DateOnly date, int hour) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0,
            TimeSpan.FromHours(9)); // JST = UTC+9

    private static DateOnly GetRaceDay2(DateOnly firstRaceDay) => firstRaceDay.AddDays(1);

    // ------------------------------------------------------------------ //
    // Phase enum
    // ------------------------------------------------------------------ //

    /// <summary>スケジューラーの実行フェーズ。</summary>
    public enum SchedulePhase
    {
        /// <summary>開催日 -2 日（朝）: レース発見 + 初回データ収集。</summary>
        Discovery,
        /// <summary>開催日 -2 日（昼）: データ再収集。</summary>
        DataRefresh,
        /// <summary>開催前日（朝）: JRA 出馬表収集。</summary>
        PreRaceCard,
        /// <summary>開催前日（夕）: 枠順確定後データ収集 + AI 予測。</summary>
        PostPosition,
        /// <summary>第1開催日（朝）: JRA 出馬表収集。</summary>
        RaceDay1Card,
        /// <summary>第1開催日（夜）: 成績収集。</summary>
        RaceDay1Results,
        /// <summary>第2開催日（朝）: JRA 出馬表収集。</summary>
        RaceDay2Card,
        /// <summary>第2開催日（夜）: 成績収集。</summary>
        RaceDay2Results,
        /// <summary>第1開催日 +2 日（朝）: 最終成績収集。</summary>
        FinalResults
    }
}
