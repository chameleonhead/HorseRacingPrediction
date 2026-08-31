namespace HorseRacingPrediction.Collector.Scheduling;

/// <summary>
/// Collector の処理分離設定。
/// </summary>
public sealed class AgentProcessingOptions
{
    public const string SectionName = "AgentProcessing";

    /// <summary>分離処理全体を有効化するかどうか。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>状態ファイル格納ディレクトリ。空の場合は実行ディレクトリ配下。</summary>
    public string StateDirectory { get; set; } = string.Empty;

    /// <summary>ローカルジョブストアの SQLite ファイル名。</summary>
    public string JobStoreFileName { get; set; } = "processing-jobs.db";

    /// <summary>スクレイピング登録の実行間隔（分）。</summary>
    public int ScrapingIntervalMinutes { get; set; } = 180;

    /// <summary>収集ジョブ実行の実行間隔（分）。</summary>
    public int CollectionExecutionIntervalMinutes { get; set; } = 15;

    /// <summary>収集ワーカーの1回実行あたりの最大処理件数。</summary>
    public int CollectionBatchSize { get; set; } = 10;

    /// <summary>Collector 全体で同時実行可能なジョブ数。</summary>
    public int MaxConcurrentJobs { get; set; } = 1;

    /// <summary>収集ジョブ実行中リースの有効期限（分）。</summary>
    public int CollectionLeaseMinutes { get; set; } = 30;

    /// <summary>予想実行サービスを有効化するかどうか。</summary>
    public bool EnablePredictionExecution { get; set; } = false;

    /// <summary>予想実行の実行間隔（分）。</summary>
    public int PredictionIntervalMinutes { get; set; } = 60;

    /// <summary>予想キューに積んだ後、予想対象として取り出すまでの最小待機時間（分）。</summary>
    public int PredictionMinAgeMinutes { get; set; } = 10;

    /// <summary>予想実行中リースの有効期限（分）。クラッシュ時の再取得に使用する。</summary>
    public int PredictionLeaseMinutes { get; set; } = 30;

    /// <summary>予想サービスの1回実行あたりの最大処理件数。</summary>
    public int PredictionBatchSize { get; set; } = 20;

    /// <summary>未解決の過去データ補完要求がある間、予想実行を待機させるかどうか。</summary>
    public bool BlockPredictionWhileHistoricalRequestsPending { get; set; } = true;

    /// <summary>過去データ補完要求待ちで予想を再投入する際の遅延（分）。</summary>
    public int HistoricalRequestRetryDelayMinutes { get; set; } = 15;

    /// <summary>過去データ補完要求実行の実行間隔（分）。</summary>
    public int HistoricalRequestExecutionIntervalMinutes { get; set; } = 15;

    /// <summary>過去データ補完要求ワーカーの1回実行あたりの最大処理件数。</summary>
    public int HistoricalRequestBatchSize { get; set; } = 10;

    /// <summary>過去データ補完要求実行中リースの有効期限（分）。</summary>
    public int HistoricalRequestLeaseMinutes { get; set; } = 30;

    /// <summary>過去データ補完要求の最大試行回数。</summary>
    public int HistoricalRequestMaxAttempts { get; set; } = 3;

    /// <summary>結果収集対象日付の遡り日数（JST基準）。</summary>
    public int ResultLookbackDays { get; set; } = 2;

    /// <summary>初回バックフィル対象年数。</summary>
    public int InitialResultBackfillYears { get; set; } = 3;

    /// <summary>開催中モード時の結果収集対象日付の遡り日数（JST基準）。</summary>
    public int LiveResultLookbackDays { get; set; } = 0;

    /// <summary>開催前モード時の結果収集対象日付の遡り日数（JST基準）。</summary>
    public int PreRaceResultLookbackDays { get; set; } = 1;

    /// <summary>結果収集対象日付の先行日数（JST基準）。通常は0。</summary>
    public int ResultLookaheadDays { get; set; } = 0;

    /// <summary>予定収集を有効化するかどうか。</summary>
    public bool EnableScheduleCollection { get; set; } = true;

    /// <summary>出馬表収集を有効化するかどうか。予想キュー投入の起点として使用する。</summary>
    public bool EnableRaceCardCollection { get; set; } = true;

    /// <summary>予定収集対象の先行日数（JST基準）。</summary>
    public int ScheduleLookaheadDays { get; set; } = 14;

    /// <summary>開催前モードへ入る先行日数（JST基準）。</summary>
    public int PreRaceLeadDays { get; set; } = 1;

    /// <summary>成績収集を有効化するかどうか。</summary>
    public bool EnableRaceResultCollection { get; set; } = true;

    /// <summary>開催中は過去向きバックフィルを抑制するかどうか。</summary>
    public bool SuppressHistoricalBackfillDuringLive { get; set; } = true;
}
