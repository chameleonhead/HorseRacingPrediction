namespace HorseRacingPrediction.Agents.Workflow;

/// <summary>
/// <see cref="WeeklyScheduleWorkflow.CollectPostPositionsAndPredictAsync"/> において
/// 1 レース分の枠順確定後データ収集と予測の結果を保持する。
/// </summary>
public sealed record WeeklyPredictionResult(
    /// <summary>対象レースの情報</summary>
    WeekendRaceInfo RaceInfo,
    /// <summary>枠順確定後に再収集したデータ（レース・馬・騎手・厩舎）</summary>
    DataCollectionResult CollectionResult,
    /// <summary>予測レポート（Markdown 形式）</summary>
    string PredictionSummary);
