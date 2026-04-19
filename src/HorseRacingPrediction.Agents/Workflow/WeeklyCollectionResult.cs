namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// <see cref="WeeklyScheduleWorkflow.CollectDataAsync"/> の実行結果。
/// 対象週末と発見された各レースのデータ収集結果を保持する。
/// </summary>
public sealed record WeeklyCollectionResult(
    /// <summary>対象の週末（土曜日の日付）</summary>
    DateOnly TargetWeekend,
    /// <summary>
    /// レースごとの収集結果。<see cref="DataCollectionResult.RaceQuery"/> で
    /// どのレースの結果かを識別できる。
    /// </summary>
    IReadOnlyList<DataCollectionResult> RaceCollections);
