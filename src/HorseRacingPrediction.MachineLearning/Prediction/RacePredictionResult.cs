namespace HorseRacingPrediction.MachineLearning.Prediction;

/// <summary>
/// レース全体の ML 予測結果（出走頭数分の <see cref="HorseRacePrediction"/> を保持）。
/// </summary>
public sealed record RacePredictionResult(
    string RaceId,
    IReadOnlyList<HorseRacePrediction> Rankings);
