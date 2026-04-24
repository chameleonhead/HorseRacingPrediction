using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.MachineLearning.Prediction;

namespace HorseRacingPrediction.MachineLearning;

/// <summary>
/// ML.NET モデルを使って出走馬の予測着順を算出するサービスの抽象。
/// </summary>
public interface IRacePredictor
{
    /// <summary>
    /// 指定したレースの全出走馬を対象に予測着順を返す。
    /// 訓練済みモデルがない場合は統計スコアで代替する。
    /// </summary>
    Task<RacePredictionResult> PredictAsync(
        RacePredictionContextReadModel raceContext,
        Func<string, CancellationToken, Task<HorseRaceHistoryReadModel?>> getHorseHistory,
        Func<string, CancellationToken, Task<JockeyRaceHistoryReadModel?>> getJockeyHistory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 過去レース結果からモデルを再訓練する。
    /// </summary>
    Task TrainAsync(
        IEnumerable<RaceResultViewReadModel> raceResults,
        Func<string, CancellationToken, Task<RacePredictionContextReadModel?>> getRaceContext,
        Func<string, CancellationToken, Task<HorseRaceHistoryReadModel?>> getHorseHistory,
        Func<string, CancellationToken, Task<JockeyRaceHistoryReadModel?>> getJockeyHistory,
        CancellationToken cancellationToken = default);

    /// <summary>訓練済みモデルをストリームに保存する。</summary>
    Task SaveModelAsync(Stream destination, CancellationToken cancellationToken = default);

    /// <summary>ストリームからモデルを読み込む。</summary>
    Task LoadModelAsync(Stream source, CancellationToken cancellationToken = default);

    /// <summary>訓練済みモデルが存在するか。</summary>
    bool IsModelTrained { get; }
}
