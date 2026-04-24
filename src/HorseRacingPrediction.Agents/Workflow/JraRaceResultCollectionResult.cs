using HorseRacingPrediction.Agents.Scrapers.Jra;

namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// <see cref="JraRaceResultCollectionWorkflow"/> の実行結果。
/// </summary>
public sealed record JraRaceResultCollectionResult(
    /// <summary>収集対象の開催日</summary>
    DateOnly RaceDate,
    /// <summary>AI エージェントが発見した成績 URL 一覧</summary>
    IReadOnlyList<JraRaceResultUrl> DiscoveredUrls,
    /// <summary>スクレイプした成績データ一覧</summary>
    IReadOnlyList<JraRaceResultData> ScrapedResults,
    /// <summary>DB に保存されたレース ID 一覧</summary>
    IReadOnlyList<string> SavedRaceIds,
    /// <summary>エラーメッセージ一覧（スキップ・失敗したレース）</summary>
    IReadOnlyList<string> Errors);
