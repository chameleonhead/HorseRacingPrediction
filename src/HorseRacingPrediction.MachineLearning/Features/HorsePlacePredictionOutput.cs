using Microsoft.ML.Data;

namespace HorseRacingPrediction.MachineLearning.Features;

/// <summary>
/// ML.NET モデルの出力（1頭分の予測着順スコア）。
/// </summary>
public sealed class HorsePlacePredictionOutput
{
    /// <summary>予測着順スコア（小さいほど上位）</summary>
    [ColumnName("Score")]
    public float Score { get; set; }
}
