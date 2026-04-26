namespace HorseRacingPrediction.AgentClient.Scheduling;

/// <summary>
/// 自動スケジューラーの設定。
/// <para>
/// 各フェーズの実行時刻は「24 時間形式の時（Hour）」で指定する。
/// すべての時刻は日本時間（JST = UTC+9）で解釈される。
/// </para>
/// <para>
/// スケジュールは <see cref="FirstRaceDayOfWeek"/> で指定した曜日を「第1開催日」として
/// その相対日数で構成される。土日開催（デフォルト）以外に、中山金杯のような
/// 任意曜日の開催にも対応できる。
/// </para>
/// </summary>
public sealed class WeeklySchedulerOptions
{
    public const string SectionName = "WeeklyScheduler";

    /// <summary>スケジューラーを有効化するかどうか。false の場合は何も実行しない。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 状態ファイル（発見レースキャッシュ）を保存するディレクトリ。
    /// 空の場合は実行ディレクトリ以下の "scheduler-state" フォルダを使用する。
    /// </summary>
    public string StateDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 第1開催日の曜日（<see cref="DayOfWeek"/> の整数値）。
    /// デフォルトは 6（土曜日）。中山金杯のように土日以外の曜日に開催される場合は変更する。
    /// </summary>
    public int FirstRaceDayOfWeek { get; set; } = (int)DayOfWeek.Saturday;

    // ------------------------------------------------------------------ //
    // 発見フェーズ（開催日の2日前）
    // ------------------------------------------------------------------ //

    /// <summary>開催日の2日前（朝）：レース発見の実行時刻（時）。デフォルト 8 時。</summary>
    public int DiscoveryHour { get; set; } = 8;

    /// <summary>開催日の2日前（昼）：データ再収集の実行時刻（時）。デフォルト 14 時。</summary>
    public int DataRefreshHour { get; set; } = 14;

    // ------------------------------------------------------------------ //
    // 前日フェーズ（開催日の前日）
    // ------------------------------------------------------------------ //

    /// <summary>開催前日（朝）：JRA 出馬表収集の実行時刻（時）。デフォルト 9 時。</summary>
    public int PreRaceCardHour { get; set; } = 9;

    /// <summary>開催前日（夕）：枠順確定後データ収集 + 予測の実行時刻（時）。デフォルト 18 時。</summary>
    public int PostPositionHour { get; set; } = 18;

    // ------------------------------------------------------------------ //
    // 第1開催日フェーズ
    // ------------------------------------------------------------------ //

    /// <summary>第1開催日（朝）：JRA 出馬表収集の実行時刻（時）。デフォルト 7 時。</summary>
    public int RaceDay1CardHour { get; set; } = 7;

    /// <summary>第1開催日（夜）：成績収集の実行時刻（時）。デフォルト 21 時。</summary>
    public int RaceDay1ResultsHour { get; set; } = 21;

    // ------------------------------------------------------------------ //
    // 第2開催日フェーズ（第1開催日の翌日）
    // ------------------------------------------------------------------ //

    /// <summary>第2開催日（朝）：JRA 出馬表収集の実行時刻（時）。デフォルト 7 時。</summary>
    public int RaceDay2CardHour { get; set; } = 7;

    /// <summary>第2開催日（夜）：成績収集の実行時刻（時）。デフォルト 21 時。</summary>
    public int RaceDay2ResultsHour { get; set; } = 21;

    // ------------------------------------------------------------------ //
    // 最終成績収集フェーズ（第1開催日の2日後）
    // ------------------------------------------------------------------ //

    /// <summary>最終成績収集日（朝）：最終成績収集の実行時刻（時）。デフォルト 9 時。</summary>
    public int FinalResultsHour { get; set; } = 9;
}
