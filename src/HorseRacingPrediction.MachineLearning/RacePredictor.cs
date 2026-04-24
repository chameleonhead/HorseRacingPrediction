using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.MachineLearning.Features;
using HorseRacingPrediction.MachineLearning.Prediction;
using HorseRacingPrediction.MachineLearning.Training;
using Microsoft.ML;
using Microsoft.ML.Trainers.FastTree;

namespace HorseRacingPrediction.MachineLearning;

/// <summary>
/// FastTree 回帰を使った競馬着順予測の ML.NET 実装。
/// <list type="bullet">
///   <item>
///     <see cref="TrainAsync"/> でモデルを訓練し、<see cref="SaveModelAsync"/> / <see cref="LoadModelAsync"/>
///     でシリアライズ可能。
///   </item>
///   <item>
///     訓練済みモデルがない場合 (<see cref="IsModelTrained"/> == false) は、
///     統計スコア（RecentAvgFinishPosition × 0.4 + HorseWinRate × -10 + ...）で代替する。
///   </item>
/// </list>
/// </summary>
public sealed class RacePredictor : IRacePredictor
{
    private readonly MLContext _mlContext;
    private ITransformer? _model;

    // 特徴量列名（HorseEntryFeatureInput のプロパティ名と一致させる）
    private static readonly string[] FeatureColumns =
    [
        nameof(HorseEntryFeatureInput.GateNumber),
        nameof(HorseEntryFeatureInput.AssignedWeight),
        nameof(HorseEntryFeatureInput.Age),
        nameof(HorseEntryFeatureInput.DeclaredWeightDiff),
        nameof(HorseEntryFeatureInput.DeclaredWeight),
        nameof(HorseEntryFeatureInput.RunningStyleCode),
        nameof(HorseEntryFeatureInput.SexCode),
        nameof(HorseEntryFeatureInput.RecentAvgFinishPosition),
        nameof(HorseEntryFeatureInput.HorseWinRate),
        nameof(HorseEntryFeatureInput.HorsePlaceRate),
        nameof(HorseEntryFeatureInput.HorseSurfaceWinRate),
        nameof(HorseEntryFeatureInput.DistanceSuitabilityScore),
        nameof(HorseEntryFeatureInput.RacecourseSuitabilityScore),
        nameof(HorseEntryFeatureInput.DirectionSuitabilityScore),
        nameof(HorseEntryFeatureInput.WeightStabilityScore),
        nameof(HorseEntryFeatureInput.AvgLastThreeFurlongTime),
        nameof(HorseEntryFeatureInput.AvgPrizeMoney),
        nameof(HorseEntryFeatureInput.AvgCornerPosition),
        nameof(HorseEntryFeatureInput.DaysFromLastRace),
        nameof(HorseEntryFeatureInput.TotalRaceCount),
        nameof(HorseEntryFeatureInput.JockeyRecentWinRate),
        nameof(HorseEntryFeatureInput.JockeyRecentPlaceRate),
        nameof(HorseEntryFeatureInput.JockeySurfaceWinRate),
        nameof(HorseEntryFeatureInput.JockeyDistanceWinRate),
        nameof(HorseEntryFeatureInput.JockeyHorseComboCount),
        nameof(HorseEntryFeatureInput.JockeyHorseComboWinRate),
        nameof(HorseEntryFeatureInput.JockeyChanged),
        nameof(HorseEntryFeatureInput.FieldLeaderCount),
        nameof(HorseEntryFeatureInput.FieldFrontRunnerCount),
        nameof(HorseEntryFeatureInput.ExpectedRacePosition),
        nameof(HorseEntryFeatureInput.FavoredPaceType),
        nameof(HorseEntryFeatureInput.FieldSizeEffect),
    ];

    public RacePredictor(MLContext? mlContext = null)
    {
        _mlContext = mlContext ?? new MLContext(seed: 42);
    }

    /// <inheritdoc />
    public bool IsModelTrained => _model is not null;

    // ------------------------------------------------------------------ //
    // Predict
    // ------------------------------------------------------------------ //

    /// <summary>
    /// 指定したレースの全出走馬を対象に予測着順を返す。
    /// </summary>
    public async Task<RacePredictionResult> PredictAsync(
        RacePredictionContextReadModel raceContext,
        Func<string, CancellationToken, Task<HorseRaceHistoryReadModel?>> getHorseHistory,
        Func<string, CancellationToken, Task<JockeyRaceHistoryReadModel?>> getJockeyHistory,
        CancellationToken cancellationToken = default)
    {
        var entries = raceContext.Entries;
        var fieldSize = entries.Count;

        var leaderCount      = entries.Count(e => e.RunningStyleCode == "逃");
        var frontRunnerCount = entries.Count(e => e.RunningStyleCode == "先");
        var paceType = leaderCount >= 3 ? "HiPace" : leaderCount == 2 ? "MidPace" : "SlowPace";
        var fieldSizeEffect  = Math.Min(100f, (fieldSize - 6) * 100f / 12f);
        var raceDate = raceContext.RaceDate ?? DateOnly.FromDateTime(DateTime.Today);

        var inputTasks = entries.Select(async entry =>
        {
            var horseHistory  = await getHorseHistory(entry.HorseId, cancellationToken).ConfigureAwait(false);
            var jockeyHistory = entry.JockeyId is null ? null
                : await getJockeyHistory(entry.JockeyId, cancellationToken).ConfigureAwait(false);
            var expectedPos   = EstimatePosition(entry.RunningStyleCode, leaderCount, frontRunnerCount);
            return (entry, FeatureMapper.Build(
                entry, horseHistory, jockeyHistory,
                leaderCount, frontRunnerCount, paceType, fieldSizeEffect, expectedPos, raceDate));
        });
        var inputs = (await Task.WhenAll(inputTasks).ConfigureAwait(false)).ToList();

        List<(string EntryId, string HorseId, int HorseNumber, float Score)> scores;

        if (_model is not null)
        {
            var engine = _mlContext.Model.CreatePredictionEngine<HorseEntryFeatureInput, HorsePlacePredictionOutput>(_model);
            scores = inputs.Select(t =>
            {
                var prediction = engine.Predict(t.Item2);
                return (t.entry.EntryId, t.entry.HorseId, t.entry.HorseNumber, prediction.Score);
            }).ToList();
        }
        else
        {
            // 訓練済みモデルがない場合は統計スコアで代替
            scores = inputs.Select(t =>
            {
                var f = t.Item2;
                var score = f.RecentAvgFinishPosition * 0.4f
                    + (1f - f.HorseWinRate) * 5f
                    + (1f - f.HorsePlaceRate) * 3f
                    + (100f - f.DistanceSuitabilityScore) * 0.02f
                    + (100f - f.RacecourseSuitabilityScore) * 0.01f
                    + (1f - f.JockeyRecentWinRate) * 2f;
                return (t.entry.EntryId, t.entry.HorseId, t.entry.HorseNumber, score);
            }).ToList();
        }

        var ranked = scores
            .OrderBy(s => s.Score)
            .Select((s, idx) => new HorseRacePrediction(
                s.EntryId, s.HorseId, s.HorseNumber,
                PredictedScore: s.Score,
                PredictedRank: idx + 1))
            .ToList();

        return new RacePredictionResult(raceContext.RaceId ?? string.Empty, ranked);
    }

    // ------------------------------------------------------------------ //
    // Train
    // ------------------------------------------------------------------ //

    /// <inheritdoc />
    public async Task TrainAsync(
        IEnumerable<RaceResultViewReadModel> raceResults,
        Func<string, CancellationToken, Task<RacePredictionContextReadModel?>> getRaceContext,
        Func<string, CancellationToken, Task<HorseRaceHistoryReadModel?>> getHorseHistory,
        Func<string, CancellationToken, Task<JockeyRaceHistoryReadModel?>> getJockeyHistory,
        CancellationToken cancellationToken = default)
    {
        var trainingData = await TrainingDataBuilder.BuildAsync(
            raceResults, getRaceContext, getHorseHistory, getJockeyHistory, cancellationToken).ConfigureAwait(false);

        if (trainingData.Count < 10)
            throw new InvalidOperationException(
                $"訓練データが不足しています（{trainingData.Count} 件）。最低10頭分のデータが必要です。");

        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

        var pipeline = BuildPipeline();
        _model = pipeline.Fit(dataView);
    }

    // ------------------------------------------------------------------ //
    // Save / Load
    // ------------------------------------------------------------------ //

    /// <inheritdoc />
    public Task SaveModelAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        if (_model is null)
            throw new InvalidOperationException("モデルが訓練されていません。");

        _mlContext.Model.Save(_model, null, destination);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task LoadModelAsync(Stream source, CancellationToken cancellationToken = default)
    {
        _model = _mlContext.Model.Load(source, out _);
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ //
    // Private helpers
    // ------------------------------------------------------------------ //

    private IEstimator<ITransformer> BuildPipeline()
    {
        var featureConcat = _mlContext.Transforms.Concatenate("Features", FeatureColumns);

        var trainer = _mlContext.Regression.Trainers.FastTree(
            new FastTreeRegressionTrainer.Options
            {
                NumberOfTrees        = 200,
                NumberOfLeaves       = 31,
                MinimumExampleCountPerLeaf = 5,
                LearningRate         = 0.05,
                LabelColumnName      = "Label",
                FeatureColumnName    = "Features",
            });

        return featureConcat.Append(trainer);
    }

    private static float EstimatePosition(string? runningStyleCode, int leaderCount, int frontRunnerCount)
    {
        return runningStyleCode switch
        {
            "逃" => 1.5f,
            "先" => 1.5f + leaderCount + (frontRunnerCount / 2f),
            "差" => 1.5f + leaderCount + frontRunnerCount + 2f,
            "追" => 1.5f + leaderCount + frontRunnerCount + 5f,
            _    => 8f,
        };
    }
}
