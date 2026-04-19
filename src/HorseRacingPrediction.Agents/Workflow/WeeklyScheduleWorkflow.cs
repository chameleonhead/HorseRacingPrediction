using HorseRacingPrediction.Agents.Agents;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// 木曜〜日曜にわたる競馬予測の週次スケジュールワークフロー。
/// <para>
/// 外部スケジューラーから各フェーズのメソッドを呼び出すことで、
/// 以下のサイクルを実現する:
/// </para>
/// <list type="number">
///   <item>
///     <b>木曜フェーズ</b>: <see cref="DiscoverRacesAsync"/> で週末レースを発見し、
///     <see cref="CollectDataAsync"/> でレース・馬・騎手・厩舎データを一括収集する。
///     数時間おきに <see cref="CollectDataAsync"/> を再実行することで情報を更新する。
///   </item>
///   <item>
///     <b>金曜夕方フェーズ</b>: 枠順確定後に <see cref="CollectPostPositionsAndPredictAsync"/>
///     を呼び出す。枠順を含む最新データを再収集してから予測レポートを作成する。
///     定期的に再実行することで予測を更新できる。
///   </item>
///   <item>
///     <b>土曜・日曜フェーズ</b>: <see cref="CollectDataAsync"/> を引き続き
///     呼び出して当日の最新情報（馬場・天候・オッズ変動など）を収集し続ける。
///   </item>
/// </list>
/// </summary>
public sealed class WeeklyScheduleWorkflow
{
    private readonly WeekendRaceDiscoveryAgent _discoveryAgent;
    private readonly DataCollectionWorkflow _dataCollectionWorkflow;
    private readonly PostPositionPredictionAgent _predictionAgent;

    public WeeklyScheduleWorkflow(
        WeekendRaceDiscoveryAgent discoveryAgent,
        DataCollectionWorkflow dataCollectionWorkflow,
        PostPositionPredictionAgent predictionAgent)
    {
        _discoveryAgent = discoveryAgent;
        _dataCollectionWorkflow = dataCollectionWorkflow;
        _predictionAgent = predictionAgent;
    }

    // ------------------------------------------------------------------ //
    // Phase 1: Thursday — race discovery
    // ------------------------------------------------------------------ //

    /// <summary>
    /// 【木曜フェーズ】指定された週末に開催されるレースを発見し、
    /// 参加馬・騎手・調教師の一覧を返す。
    /// </summary>
    /// <param name="targetWeekend">
    /// 対象週末内のいずれかの日付。土曜日以外が指定された場合は
    /// 自動的にその週の土曜日に調整される。
    /// </param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>発見したレース一覧（取得できなかった場合は空リスト）</returns>
    public async Task<IReadOnlyList<WeekendRaceInfo>> DiscoverRacesAsync(
        DateOnly targetWeekend,
        CancellationToken cancellationToken = default)
    {
        return await _discoveryAgent.DiscoverAsync(targetWeekend, cancellationToken);
    }

    // ------------------------------------------------------------------ //
    // Phase 2: Thursday / Saturday / Sunday — data collection
    // ------------------------------------------------------------------ //

    /// <summary>
    /// 【木曜・土曜・日曜フェーズ】発見されたレースごとにウェブからデータを一括収集する。
    /// <para>
    /// 複数レースは並列実行される。数時間おきに繰り返し呼び出すことで、
    /// 馬場状態・調教情報・オッズ変動など最新情報に更新できる。
    /// </para>
    /// </summary>
    /// <param name="targetWeekend">対象の週末（土曜日の日付）</param>
    /// <param name="races"><see cref="DiscoverRacesAsync"/> で取得したレース一覧</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>各レースのデータ収集結果</returns>
    public async Task<WeeklyCollectionResult> CollectDataAsync(
        DateOnly targetWeekend,
        IReadOnlyList<WeekendRaceInfo> races,
        CancellationToken cancellationToken = default)
    {
        var collectionTasks = races.Select(race =>
            _dataCollectionWorkflow.CollectAsync(
                raceQuery: race.RaceQuery,
                horseNames: race.HorseNames,
                jockeyNames: race.JockeyNames,
                trainerNames: race.TrainerNames,
                cancellationToken: cancellationToken));

        var results = await Task.WhenAll(collectionTasks);
        return new WeeklyCollectionResult(targetWeekend, results);
    }

    // ------------------------------------------------------------------ //
    // Phase 3: Friday evening — post-position data + prediction
    // ------------------------------------------------------------------ //

    /// <summary>
    /// 【金曜夕方フェーズ】枠順確定後のレース情報を再収集し、各レースの予測を実行する。
    /// <para>
    /// 複数レースは並列実行される。定期的に再実行することで予測を更新できる。
    /// </para>
    /// </summary>
    /// <param name="races"><see cref="DiscoverRacesAsync"/> で取得したレース一覧</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>各レースの枠順確定後データ収集結果と予測レポート</returns>
    public async Task<IReadOnlyList<WeeklyPredictionResult>> CollectPostPositionsAndPredictAsync(
        IReadOnlyList<WeekendRaceInfo> races,
        CancellationToken cancellationToken = default)
    {
        var tasks = races.Select(race => CollectAndPredictOneAsync(race, cancellationToken));
        return await Task.WhenAll(tasks);
    }

    // ------------------------------------------------------------------ //
    // Factory
    // ------------------------------------------------------------------ //

    /// <summary>
    /// <see cref="WeeklyScheduleWorkflow"/> を DI なしで構築するファクトリメソッド。
    /// </summary>
    /// <param name="chatClient">共通の <see cref="IChatClient"/> インスタンス</param>
    /// <param name="webBrowserAgent">Web 検索を委譲する <see cref="Agents.WebBrowserAgent"/></param>
    /// <param name="calendarTools">日時・カレンダーツール（省略時は既定値で生成）</param>
    public static WeeklyScheduleWorkflow Create(
        IChatClient chatClient,
        WebBrowserAgent webBrowserAgent,
        CalendarTools? calendarTools = null)
    {
        calendarTools ??= new CalendarTools();
        var browseWebTool = webBrowserAgent.CreateAIFunction();

        var discoveryTools = new List<AITool> { browseWebTool };
        discoveryTools.AddRange(calendarTools.GetAITools());

        var dataTools = new List<AITool> { browseWebTool };

        return new WeeklyScheduleWorkflow(
            new WeekendRaceDiscoveryAgent(chatClient, discoveryTools),
            DataCollectionWorkflow.Create(chatClient, webBrowserAgent),
            new PostPositionPredictionAgent(chatClient, dataTools));
    }

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private async Task<WeeklyPredictionResult> CollectAndPredictOneAsync(
        WeekendRaceInfo race,
        CancellationToken cancellationToken)
    {
        // 枠順確定後のデータ収集（RaceDataAgent が枠番も含めて再取得）
        var collectionResult = await _dataCollectionWorkflow.CollectAsync(
            raceQuery: race.RaceQuery,
            horseNames: race.HorseNames,
            jockeyNames: race.JockeyNames,
            trainerNames: race.TrainerNames,
            cancellationToken: cancellationToken);

        // 収集データをもとに予測を実行
        var prediction = await _predictionAgent.PredictAsync(
            raceName: race.RaceName,
            raceData: collectionResult.RaceData,
            horseDataByName: collectionResult.HorseDataByName,
            jockeyDataByName: collectionResult.JockeyDataByName,
            stableDataByName: collectionResult.StableDataByName,
            cancellationToken: cancellationToken);

        return new WeeklyPredictionResult(race, collectionResult, prediction);
    }
}
