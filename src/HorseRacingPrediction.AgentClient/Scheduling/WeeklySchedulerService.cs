using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Scheduling;

/// <summary>
/// 木曜〜月曜にわたる競馬予測の週次サイクルを自律的に実行するバックグラウンドサービス。
/// <para>
/// <see cref="WeeklySchedulerOptions"/> で設定した時刻に従い、以下のフェーズを自動実行する。
/// </para>
/// <list type="number">
///   <item>
///     <b>木曜 08:00</b>: <c>JraRaceCardCollectionWorkflow.CollectAsync()</c> で出馬表 URL を発見・保存。
///     続いて <c>WeeklyScheduleWorkflow.DiscoverRacesAsync()</c> で AI がレース一覧を構築し、
///     <c>CollectDataAsync()</c> で馬・騎手・厩舎データを一括収集する。
///   </item>
///   <item>
///     <b>木曜 14:00</b>: データ再収集（データ更新のため）。
///   </item>
///   <item>
///     <b>金曜 09:00</b>: <c>JraRaceCardCollectionWorkflow.CollectAsync()</c> で出馬表を再取得。
///   </item>
///   <item>
///     <b>金曜 18:00</b>: <c>WeeklyScheduleWorkflow.CollectPostPositionsAndPredictAsync()</c> で
///     枠順確定後のデータを収集し、AI が予測レポートを作成する。
///   </item>
///   <item>
///     <b>土曜 07:00</b>: <c>JraRaceCardCollectionWorkflow.CollectAsync()</c> で当日出馬表を更新。
///   </item>
///   <item>
///     <b>土曜 21:00</b>: <c>JraRaceResultCollectionWorkflow.CollectAsync()</c> で当日成績を収集。
///   </item>
///   <item>
///     <b>日曜 07:00</b>: 土曜と同様に当日出馬表を更新。
///   </item>
///   <item>
///     <b>日曜 21:00</b>: 当日成績を収集。
///   </item>
///   <item>
///     <b>月曜 09:00</b>: 最終成績を収集して週次サイクルを完結。
///   </item>
/// </list>
/// <para>
/// 木曜に発見したレース情報は <see cref="WeeklyStateStore"/> に JSON ファイルとして保存され、
/// アプリ再起動後も金曜以降のフェーズで再利用できる。
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
            var (nextTime, phase, saturday) = GetNextScheduledItemWithWeekend(NowJst());
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

            await ExecutePhaseAsync(phase, saturday, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("WeeklySchedulerService を停止しました。");
    }

    // ------------------------------------------------------------------ //
    // Phase execution
    // ------------------------------------------------------------------ //

    private async Task ExecutePhaseAsync(
        SchedulePhase phase,
        DateOnly saturday,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[{Phase}] フェーズを開始します (対象週末: {Saturday})", phase, saturday);

        try
        {
            switch (phase)
            {
                case SchedulePhase.ThursdayDiscovery:
                    await RunThursdayDiscoveryAsync(saturday, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.ThursdayRefresh:
                    await RunDataRefreshAsync(saturday, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.FridayRaceCard:
                    await RunRaceCardCollectionAsync(saturday, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.FridayPostPosition:
                    await RunPostPositionPredictionAsync(saturday, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.SaturdayRaceCard:
                    await RunRaceCardCollectionAsync(saturday, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.SaturdayResults:
                    var saturdayDate = GetSaturday(saturday);
                    await RunResultCollectionAsync(saturdayDate, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.SundayRaceCard:
                    var sunday = GetSunday(saturday);
                    await RunRaceCardCollectionAsync(sunday, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.SundayResults:
                    var sundayDate = GetSunday(saturday);
                    await RunResultCollectionAsync(sundayDate, cancellationToken).ConfigureAwait(false);
                    break;

                case SchedulePhase.MondayResults:
                    // 土・日両日の成績を収集（月曜の最終確定分）
                    var sat = GetSaturday(saturday);
                    var sun = GetSunday(saturday);
                    await RunResultCollectionAsync(sat, cancellationToken).ConfigureAwait(false);
                    await RunResultCollectionAsync(sun, cancellationToken).ConfigureAwait(false);
                    _stateStore.DeleteState(saturday);
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

    /// <summary>木曜フェーズ: AI によるレース発見 + 初回データ収集</summary>
    private async Task RunThursdayDiscoveryAsync(DateOnly saturday, CancellationToken ct)
    {
        // JRA 出馬表 URL の発見・スクレイプ・DB 保存
        _logger.LogInformation("[木曜] JRA 出馬表収集を開始します (週末: {Saturday})", saturday);
        var raceCardResult = await _raceCardWorkflow.CollectAsync(saturday, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "[木曜] 出馬表収集完了: {SavedCount} レース保存 (エラー {ErrorCount} 件)",
            raceCardResult.SavedRaceIds.Count, raceCardResult.Errors.Count);

        // AI によるレース発見
        _logger.LogInformation("[木曜] AI によるレース発見を開始します。");
        var races = await _weeklyWorkflow.DiscoverRacesAsync(saturday, ct).ConfigureAwait(false);
        _logger.LogInformation("[木曜] {Count} レースを発見しました。", races.Count);

        if (races.Count == 0)
        {
            _logger.LogWarning("[木曜] レースが発見されませんでした。データ収集をスキップします。");
            return;
        }

        // 状態を保存（金曜フェーズで再利用）
        await _stateStore.SaveRacesAsync(saturday, races, ct).ConfigureAwait(false);

        // 初回データ収集
        _logger.LogInformation("[木曜] 初回データ収集を開始します ({Count} レース)。", races.Count);
        var collectionResult = await _weeklyWorkflow.CollectDataAsync(saturday, races, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "[木曜] データ収集完了: {Count} レース分のデータを収集しました。",
            collectionResult.RaceCollections.Count);
    }

    /// <summary>データ再収集（木曜午後・金曜朝等）</summary>
    private async Task RunDataRefreshAsync(DateOnly saturday, CancellationToken ct)
    {
        var races = await _stateStore.LoadRacesAsync(saturday, ct).ConfigureAwait(false);

        if (races is null || races.Count == 0)
        {
            _logger.LogWarning("保存済みレース情報がありません。レース発見から再実行します。");
            await RunThursdayDiscoveryAsync(saturday, ct).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation("[データ再収集] {Count} レースのデータを再収集します。", races.Count);
        var result = await _weeklyWorkflow.CollectDataAsync(saturday, races, ct).ConfigureAwait(false);
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

    /// <summary>金曜夕方フェーズ: 枠順確定後データ収集 + AI 予測</summary>
    private async Task RunPostPositionPredictionAsync(DateOnly saturday, CancellationToken ct)
    {
        var races = await _stateStore.LoadRacesAsync(saturday, ct).ConfigureAwait(false);

        if (races is null || races.Count == 0)
        {
            _logger.LogWarning(
                "[金曜予測] 保存済みレース情報がありません。木曜フェーズを先に実行してください。");
            return;
        }

        _logger.LogInformation("[金曜予測] 枠順確定後データ収集 + 予測を開始します ({Count} レース)。", races.Count);
        var results = await _weeklyWorkflow
            .CollectPostPositionsAndPredictAsync(races, ct)
            .ConfigureAwait(false);

        foreach (var result in results)
        {
            _logger.LogInformation(
                "[金曜予測] {RaceName} ({Racecourse}) の予測が完了しました。",
                result.RaceInfo.RaceName, result.RaceInfo.Racecourse);
            _logger.LogDebug("[金曜予測] 予測サマリー: {Summary}", result.PredictionSummary);
        }

        _logger.LogInformation("[金曜予測] 全 {Count} レースの予測が完了しました。", results.Count);
    }

    /// <summary>成績収集（土・日・月）</summary>
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
        var (nextTime, phase, _) = GetNextScheduledItemWithWeekend(nowJst);
        return (nextTime, phase);
    }

    private (DateTimeOffset NextTime, SchedulePhase Phase, DateOnly Saturday) GetNextScheduledItemWithWeekend(
        DateTimeOffset nowJst)
    {
        var candidates = BuildScheduleForCurrentAndNextWeek(nowJst);

        // 現在時刻より後の最初のスケジュールを返す
        var next = candidates
            .Where(c => c.Time > nowJst)
            .OrderBy(c => c.Time)
            .First();

        return (next.Time, next.Phase, next.Saturday);
    }

    private IEnumerable<(DateTimeOffset Time, SchedulePhase Phase, DateOnly Saturday)> BuildScheduleForCurrentAndNextWeek(
        DateTimeOffset nowJst)
    {
        var today = DateOnly.FromDateTime(nowJst.DateTime);

        // 次または当日の土曜日（土曜なら当日）
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)today.DayOfWeek + 7) % 7;
        var upcomingOrCurrentSaturday = today.AddDays(daysUntilSaturday);

        // 前週・今週・来週の 3 週分を生成することで、
        // 日曜・月曜においても前週の残スケジュール（MondayResults等）が正しく返される。
        foreach (var item in BuildWeekSchedule(upcomingOrCurrentSaturday.AddDays(-7)))
            yield return item;
        foreach (var item in BuildWeekSchedule(upcomingOrCurrentSaturday))
            yield return item;
        foreach (var item in BuildWeekSchedule(upcomingOrCurrentSaturday.AddDays(7)))
            yield return item;
    }

    private IEnumerable<(DateTimeOffset Time, SchedulePhase Phase, DateOnly Saturday)> BuildWeekSchedule(DateOnly saturday)
    {
        var thursday = saturday.AddDays(-2);
        var friday = saturday.AddDays(-1);
        var sunday = saturday.AddDays(1);
        var monday = saturday.AddDays(2);

        yield return (ToJst(thursday, _options.ThursdayDiscoveryHour), SchedulePhase.ThursdayDiscovery, saturday);
        yield return (ToJst(thursday, _options.ThursdayRefreshHour), SchedulePhase.ThursdayRefresh, saturday);
        yield return (ToJst(friday, _options.FridayRaceCardHour), SchedulePhase.FridayRaceCard, saturday);
        yield return (ToJst(friday, _options.FridayPostPositionHour), SchedulePhase.FridayPostPosition, saturday);
        yield return (ToJst(saturday, _options.SaturdayRaceCardHour), SchedulePhase.SaturdayRaceCard, saturday);
        yield return (ToJst(saturday, _options.SaturdayResultsHour), SchedulePhase.SaturdayResults, saturday);
        yield return (ToJst(sunday, _options.SundayRaceCardHour), SchedulePhase.SundayRaceCard, saturday);
        yield return (ToJst(sunday, _options.SundayResultsHour), SchedulePhase.SundayResults, saturday);
        yield return (ToJst(monday, _options.MondayResultsHour), SchedulePhase.MondayResults, saturday);
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private DateTimeOffset NowJst() =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Jst);

    private static DateTimeOffset ToJst(DateOnly date, int hour) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0,
            TimeSpan.FromHours(9)); // JST = UTC+9

    private static DateOnly GetSaturday(DateOnly saturday) => saturday;
    private static DateOnly GetSunday(DateOnly saturday) => saturday.AddDays(1);

    // ------------------------------------------------------------------ //
    // Phase enum
    // ------------------------------------------------------------------ //

    /// <summary>週次スケジューラーの実行フェーズ。</summary>
    public enum SchedulePhase
    {
        ThursdayDiscovery,
        ThursdayRefresh,
        FridayRaceCard,
        FridayPostPosition,
        SaturdayRaceCard,
        SaturdayResults,
        SundayRaceCard,
        SundayResults,
        MondayResults
    }
}
