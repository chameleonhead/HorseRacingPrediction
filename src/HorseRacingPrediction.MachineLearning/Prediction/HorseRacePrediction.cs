namespace HorseRacingPrediction.MachineLearning.Prediction;

/// <summary>
/// 1頭分の ML 予測結果。
/// </summary>
public sealed record HorseRacePrediction(
    string EntryId,
    string HorseId,
    int HorseNumber,
    float PredictedScore,
    int PredictedRank);
