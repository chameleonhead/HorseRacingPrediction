namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// <see cref="DataCollectionWorkflow.CollectAsync"/> の実行結果。
/// 各データ収集エージェントの出力を保持する。
/// </summary>
public sealed record DataCollectionResult(
    /// <summary>対象レースのクエリ文字列</summary>
    string RaceQuery,
    /// <summary>レース情報（RaceDataAgent の出力 Markdown）</summary>
    string RaceData,
    /// <summary>馬情報の辞書（キー: 馬名、値: HorseDataAgent の出力 Markdown）</summary>
    IReadOnlyDictionary<string, string> HorseDataByName,
    /// <summary>騎手情報の辞書（キー: 騎手名、値: JockeyDataAgent の出力 Markdown）</summary>
    IReadOnlyDictionary<string, string> JockeyDataByName,
    /// <summary>厩舎情報の辞書（キー: 調教師名、値: StableDataAgent の出力 Markdown）</summary>
    IReadOnlyDictionary<string, string> StableDataByName);
