namespace HorseRacingPrediction.AgentClient.Scheduling;

/// <summary>
/// 週次自動スケジューラーの設定。
/// <para>
/// 各フェーズの実行時刻は「24 時間形式の時（Hour）」で指定する。
/// すべての時刻は日本時間（JST = UTC+9）で解釈される。
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

    // ------------------------------------------------------------------ //
    // 木曜フェーズ（レース発見 + 初回データ収集）
    // ------------------------------------------------------------------ //

    /// <summary>木曜日：レース発見の実行時刻（時）。デフォルト 8 時。</summary>
    public int ThursdayDiscoveryHour { get; set; } = 8;

    /// <summary>木曜日：データ再収集の実行時刻（時）。デフォルト 14 時。</summary>
    public int ThursdayRefreshHour { get; set; } = 14;

    // ------------------------------------------------------------------ //
    // 金曜フェーズ（出馬表収集 → 枠順確定後予測）
    // ------------------------------------------------------------------ //

    /// <summary>金曜日：JRA 出馬表収集の実行時刻（時）。デフォルト 9 時。</summary>
    public int FridayRaceCardHour { get; set; } = 9;

    /// <summary>金曜日：枠順確定後データ収集 + 予測の実行時刻（時）。デフォルト 18 時。</summary>
    public int FridayPostPositionHour { get; set; } = 18;

    // ------------------------------------------------------------------ //
    // 土曜フェーズ（出馬表更新 + 当日成績収集）
    // ------------------------------------------------------------------ //

    /// <summary>土曜日：JRA 出馬表収集の実行時刻（時）。デフォルト 7 時。</summary>
    public int SaturdayRaceCardHour { get; set; } = 7;

    /// <summary>土曜日：成績収集の実行時刻（時）。デフォルト 21 時。</summary>
    public int SaturdayResultsHour { get; set; } = 21;

    // ------------------------------------------------------------------ //
    // 日曜フェーズ（出馬表更新 + 当日成績収集）
    // ------------------------------------------------------------------ //

    /// <summary>日曜日：JRA 出馬表収集の実行時刻（時）。デフォルト 7 時。</summary>
    public int SundayRaceCardHour { get; set; } = 7;

    /// <summary>日曜日：成績収集の実行時刻（時）。デフォルト 21 時。</summary>
    public int SundayResultsHour { get; set; } = 21;

    // ------------------------------------------------------------------ //
    // 月曜フェーズ（最終成績取り込み）
    // ------------------------------------------------------------------ //

    /// <summary>月曜日：最終成績収集の実行時刻（時）。デフォルト 9 時。</summary>
    public int MondayResultsHour { get; set; } = 9;
}
