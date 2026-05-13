namespace HorseRacingPrediction.AgentClient.Scheduling;

/// <summary>
/// AgentClient の処理分離設定。
/// </summary>
public sealed class AgentProcessingOptions
{
    public const string SectionName = "AgentProcessing";

    /// <summary>分離処理全体を有効化するかどうか。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>状態ファイル格納ディレクトリ。空の場合は実行ディレクトリ配下。</summary>
    public string StateDirectory { get; set; } = string.Empty;

    /// <summary>スクレイピング登録の実行間隔（分）。</summary>
    public int ScrapingIntervalMinutes { get; set; } = 180;

    /// <summary>予想実行の実行間隔（分）。</summary>
    public int PredictionIntervalMinutes { get; set; } = 60;

    /// <summary>予想キューに積んだ後、予想対象として取り出すまでの最小待機時間（分）。</summary>
    public int PredictionMinAgeMinutes { get; set; } = 10;

    /// <summary>予想実行中リースの有効期限（分）。クラッシュ時の再取得に使用する。</summary>
    public int PredictionLeaseMinutes { get; set; } = 30;

    /// <summary>予想サービスの1回実行あたりの最大処理件数。</summary>
    public int PredictionBatchSize { get; set; } = 20;

    /// <summary>結果収集対象日付の遡り日数（JST基準）。</summary>
    public int ResultLookbackDays { get; set; } = 2;

    /// <summary>結果収集対象日付の先行日数（JST基準）。通常は0。</summary>
    public int ResultLookaheadDays { get; set; } = 0;

    /// <summary>予定収集を有効化するかどうか。</summary>
    public bool EnableScheduleCollection { get; set; } = true;

    /// <summary>出馬表収集を有効化するかどうか。予想キュー投入の起点として使用する。</summary>
    public bool EnableRaceCardCollection { get; set; } = true;

    /// <summary>予定収集対象の先行日数（JST基準）。</summary>
    public int ScheduleLookaheadDays { get; set; } = 14;

    /// <summary>成績収集を有効化するかどうか。</summary>
    public bool EnableRaceResultCollection { get; set; } = true;

    /// <summary>任意テキスト収集（Memo登録）を有効化するかどうか。</summary>
    public bool EnableTextInsightCollection { get; set; } = true;

    /// <summary>レースごとの任意テキスト収集クエリテンプレート。</summary>
    public List<string> TextInsightQueryTemplates { get; set; } =
    [
        "{RaceDate} {RacecourseCode} {RaceNumber}R {RaceName} 展望",
        "{RaceDate} {RacecourseCode} {RaceNumber}R 馬場傾向",
        "{RaceDate} {RacecourseCode} {RaceNumber}R 注目馬"
    ];
}
