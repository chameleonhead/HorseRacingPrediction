using Microsoft.ML.Data;

namespace HorseRacingPrediction.MachineLearning.Features;

/// <summary>
/// ML.NET モデルへの入力特徴量（1頭分）。
/// 30+パラメーターをすべて float にフラット化して保持する。
/// </summary>
public sealed class HorseEntryFeatureInput
{
    // ------------------------------------------------------------------ //
    // Group A: 基本出走データ
    // ------------------------------------------------------------------ //

    /// <summary>枠番（1-8, 不明=0）</summary>
    [LoadColumn(0)]
    public float GateNumber { get; set; }

    /// <summary>斤量（kg）</summary>
    [LoadColumn(1)]
    public float AssignedWeight { get; set; }

    /// <summary>馬齢</summary>
    [LoadColumn(2)]
    public float Age { get; set; }

    /// <summary>体重変化（直前計量差 kg）</summary>
    [LoadColumn(3)]
    public float DeclaredWeightDiff { get; set; }

    /// <summary>申告体重（kg）</summary>
    [LoadColumn(4)]
    public float DeclaredWeight { get; set; }

    /// <summary>脚質コード（逃=1, 先=2, 差=3, 追=4, 不明=0）</summary>
    [LoadColumn(5)]
    public float RunningStyleCode { get; set; }

    /// <summary>性別コード（牡=1, 牝=2, 騸=3, 不明=0）</summary>
    [LoadColumn(6)]
    public float SexCode { get; set; }

    // ------------------------------------------------------------------ //
    // Group B: 馬パフォーマンス統計
    // ------------------------------------------------------------------ //

    /// <summary>直近5走平均着順</summary>
    [LoadColumn(7)]
    public float RecentAvgFinishPosition { get; set; }

    /// <summary>勝率（0.0-1.0）</summary>
    [LoadColumn(8)]
    public float HorseWinRate { get; set; }

    /// <summary>複勝率（0.0-1.0）</summary>
    [LoadColumn(9)]
    public float HorsePlaceRate { get; set; }

    /// <summary>馬場種別勝率（0.0-1.0）</summary>
    [LoadColumn(10)]
    public float HorseSurfaceWinRate { get; set; }

    /// <summary>距離適性スコア（0-100）</summary>
    [LoadColumn(11)]
    public float DistanceSuitabilityScore { get; set; }

    /// <summary>競馬場適性スコア（0-100）</summary>
    [LoadColumn(12)]
    public float RacecourseSuitabilityScore { get; set; }

    /// <summary>回り適性スコア（0-100）</summary>
    [LoadColumn(13)]
    public float DirectionSuitabilityScore { get; set; }

    /// <summary>体重安定度スコア（0-10）</summary>
    [LoadColumn(14)]
    public float WeightStabilityScore { get; set; }

    /// <summary>平均上がり3Fタイム（秒）</summary>
    [LoadColumn(15)]
    public float AvgLastThreeFurlongTime { get; set; }

    /// <summary>平均賞金（万円単位に正規化）</summary>
    [LoadColumn(16)]
    public float AvgPrizeMoney { get; set; }

    /// <summary>平均最終コーナー順位</summary>
    [LoadColumn(17)]
    public float AvgCornerPosition { get; set; }

    /// <summary>前走からの間隔日数（初出走=999）</summary>
    [LoadColumn(18)]
    public float DaysFromLastRace { get; set; }

    /// <summary>総出走数</summary>
    [LoadColumn(19)]
    public float TotalRaceCount { get; set; }

    // ------------------------------------------------------------------ //
    // Group C: 騎手統計
    // ------------------------------------------------------------------ //

    /// <summary>騎手直近20走勝率（0.0-1.0）</summary>
    [LoadColumn(20)]
    public float JockeyRecentWinRate { get; set; }

    /// <summary>騎手直近20走複勝率（0.0-1.0）</summary>
    [LoadColumn(21)]
    public float JockeyRecentPlaceRate { get; set; }

    /// <summary>騎手×馬場勝率（0.0-1.0）</summary>
    [LoadColumn(22)]
    public float JockeySurfaceWinRate { get; set; }

    /// <summary>騎手×距離勝率（0.0-1.0）</summary>
    [LoadColumn(23)]
    public float JockeyDistanceWinRate { get; set; }

    /// <summary>騎手×馬コンビ出走数</summary>
    [LoadColumn(24)]
    public float JockeyHorseComboCount { get; set; }

    /// <summary>騎手×馬コンビ勝率（0.0-1.0）</summary>
    [LoadColumn(25)]
    public float JockeyHorseComboWinRate { get; set; }

    /// <summary>騎手乗替わりフラグ（乗替=1, 継続=0）</summary>
    [LoadColumn(26)]
    public float JockeyChanged { get; set; }

    // ------------------------------------------------------------------ //
    // Group D: レース展開
    // ------------------------------------------------------------------ //

    /// <summary>フィールド内逃げ馬頭数</summary>
    [LoadColumn(27)]
    public float FieldLeaderCount { get; set; }

    /// <summary>フィールド内先行馬頭数</summary>
    [LoadColumn(28)]
    public float FieldFrontRunnerCount { get; set; }

    /// <summary>予想道中ポジション（1番手=1, …）</summary>
    [LoadColumn(29)]
    public float ExpectedRacePosition { get; set; }

    /// <summary>予想ペースタイプ（SlowPace=0, MidPace=1, HiPace=2）</summary>
    [LoadColumn(30)]
    public float FavoredPaceType { get; set; }

    /// <summary>出走頭数効果スコア（0-100）</summary>
    [LoadColumn(31)]
    public float FieldSizeEffect { get; set; }

    // ------------------------------------------------------------------ //
    // ラベル（学習時のみ使用）
    // ------------------------------------------------------------------ //

    /// <summary>実際の着順（学習ラベル）</summary>
    [LoadColumn(32)]
    [ColumnName("Label")]
    public float FinishPosition { get; set; }
}
