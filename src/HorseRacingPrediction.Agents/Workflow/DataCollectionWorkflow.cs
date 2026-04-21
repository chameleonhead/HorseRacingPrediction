using HorseRacingPrediction.Agents.Agents;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// 競馬予測に必要なデータをウェブから一括収集するワークフロー。
/// <para>
/// 4つのデータ収集エージェント（レース・馬・騎手・厩舎）を並列実行し、
/// 収集した情報を <see cref="DataCollectionResult"/> として返す。
/// それぞれのエージェントはウェブ検索エージェント（<see cref="Plugins.WebFetchTools"/>）を
/// ツールとして使用してインターネットから情報を取得する。
/// </para>
/// <list type="bullet">
///   <item><see cref="RaceDataAgent"/> — レース基本情報・出馬表・過去傾向</item>
///   <item><see cref="HorseDataAgent"/> — 出走馬の戦績・血統・適性</item>
///   <item><see cref="JockeyDataAgent"/> — 騎手の成績・得意傾向</item>
///   <item><see cref="StableDataAgent"/> — 厩舎・調教師の成績・傾向</item>
/// </list>
/// </summary>
public sealed class DataCollectionWorkflow
{
    private readonly RaceDataAgent _raceDataAgent;
    private readonly HorseDataAgent _horseDataAgent;
    private readonly JockeyDataAgent _jockeyDataAgent;
    private readonly StableDataAgent _stableDataAgent;

    public DataCollectionWorkflow(
        RaceDataAgent raceDataAgent,
        HorseDataAgent horseDataAgent,
        JockeyDataAgent jockeyDataAgent,
        StableDataAgent stableDataAgent)
    {
        _raceDataAgent = raceDataAgent;
        _horseDataAgent = horseDataAgent;
        _jockeyDataAgent = jockeyDataAgent;
        _stableDataAgent = stableDataAgent;
    }

    /// <summary>
    /// 指定したレース・出走馬・騎手・調教師についてウェブからデータを一括収集する。
    /// 4つのエージェントは並列で実行される。
    /// </summary>
    /// <param name="raceQuery">レースを特定するクエリ（例: "2024年天皇賞秋"）</param>
    /// <param name="horseNames">出走馬名一覧</param>
    /// <param name="jockeyNames">騎手名一覧</param>
    /// <param name="trainerNames">調教師名一覧</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>収集したデータをまとめた <see cref="DataCollectionResult"/></returns>
    public async Task<DataCollectionResult> CollectAsync(
        string raceQuery,
        IReadOnlyList<string> horseNames,
        IReadOnlyList<string> jockeyNames,
        IReadOnlyList<string> trainerNames,
        CancellationToken cancellationToken = default)
    {
        // まずレース情報を取得し、出馬表を整理して収集対象を確定する。
        var raceData = await _raceDataAgent.CollectAsync(raceQuery, cancellationToken);
        var raceCardEntries = RaceCardTableParser.Parse(raceData);

        var effectiveHorseNames = raceCardEntries
            .Select(entry => entry.HorseName)
            .Concat(horseNames)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var effectiveJockeyNames = raceCardEntries
            .Select(entry => entry.JockeyName)
            .Concat(jockeyNames)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var effectiveTrainerNames = raceCardEntries
            .Select(entry => entry.TrainerName)
            .Concat(trainerNames)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var horseTasks = effectiveHorseNames
            .Select(name => (name, task: _horseDataAgent.CollectAsync(name, cancellationToken)))
            .ToList();

        var jockeyTasks = effectiveJockeyNames
            .Select(name => (name, task: _jockeyDataAgent.CollectAsync(name, cancellationToken)))
            .ToList();

        var trainerTasks = effectiveTrainerNames
            .Select(name => (name, task: _stableDataAgent.CollectAsync(name, cancellationToken)))
            .ToList();

        var allTasks = new List<Task>();
        allTasks.AddRange(horseTasks.Select(t => t.task));
        allTasks.AddRange(jockeyTasks.Select(t => t.task));
        allTasks.AddRange(trainerTasks.Select(t => t.task));

        await Task.WhenAll(allTasks);

        var horseDataByName = horseTasks.ToDictionary(
            t => t.name,
            t => t.task.Result);

        var jockeyDataByName = jockeyTasks.ToDictionary(
            t => t.name,
            t => t.task.Result);

        var stableDataByName = trainerTasks.ToDictionary(
            t => t.name,
            t => t.task.Result);

        return new DataCollectionResult(
            raceQuery,
            raceData,
            horseDataByName,
            jockeyDataByName,
            stableDataByName);
    }

    /// <summary>
    /// <see cref="DataCollectionWorkflow"/> を DI なしで構築するファクトリメソッド。
    /// </summary>
    /// <param name="chatClient">共通の <see cref="IChatClient"/> インスタンス</param>
    /// <param name="webBrowserAgent">Web 検索を委譲する <see cref="WebBrowserAgent"/></param>
    public static DataCollectionWorkflow Create(
        IChatClient chatClient,
        WebBrowserAgent webBrowserAgent)
    {
        var tools = new List<AITool> { webBrowserAgent.CreateAIFunction() };

        return new DataCollectionWorkflow(
            new RaceDataAgent(chatClient, tools),
            new HorseDataAgent(chatClient, tools),
            new JockeyDataAgent(chatClient, tools),
            new StableDataAgent(chatClient, tools));
    }
}
