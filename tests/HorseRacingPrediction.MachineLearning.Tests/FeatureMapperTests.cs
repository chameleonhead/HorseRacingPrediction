using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.MachineLearning;
using HorseRacingPrediction.MachineLearning.Features;
using HorseRacingPrediction.MachineLearning.Training;

namespace HorseRacingPrediction.MachineLearning.Tests;

[TestClass]
public class FeatureMapperTests
{
    [TestMethod]
    public void Build_WithAllDefaults_ReturnsDefaultFeatures()
    {
        var entry = MakeEntry("e1", "h1", 1, gateNumber: 3, assignedWeight: 57m,
            age: 4, declaredWeight: 460m, declaredWeightDiff: -2m, runningStyleCode: "先", sexCode: "M");

        var input = FeatureMapper.Build(entry, null, null, 1, 2, "MidPace", 50f, 3f);

        Assert.AreEqual(3f, input.GateNumber);
        Assert.AreEqual(57f, input.AssignedWeight);
        Assert.AreEqual(4f, input.Age);
        Assert.AreEqual(-2f, input.DeclaredWeightDiff);
        Assert.AreEqual(2f, input.RunningStyleCode, "先=2");
        Assert.AreEqual(1f, input.SexCode, "M=1");
        Assert.AreEqual(10f, input.RecentAvgFinishPosition, "馬履歴なし→10");
        Assert.AreEqual(0f, input.HorseWinRate, "馬履歴なし→0");
        Assert.AreEqual(1f, input.FavoredPaceType, "MidPace=1");
        Assert.AreEqual(50f, input.FieldSizeEffect);
    }

    [TestMethod]
    public void Build_WithRunningStyleHayai_MapsToOne()
    {
        var entry = MakeEntry("e1", "h1", 1, runningStyleCode: "逃");

        var input = FeatureMapper.Build(entry, null, null, 2, 3, "HiPace", 80f, 1f);

        Assert.AreEqual(1f, input.RunningStyleCode, "逃=1");
        Assert.AreEqual(2f, input.FavoredPaceType, "HiPace=2");
    }

    [TestMethod]
    public void Build_WithUnknownRunningStyle_MapsToZero()
    {
        var entry = MakeEntry("e1", "h1", 1, runningStyleCode: null);

        var input = FeatureMapper.Build(entry, null, null, 0, 0, "SlowPace", 0f, 5f);

        Assert.AreEqual(0f, input.RunningStyleCode, "不明=0");
        Assert.AreEqual(0f, input.FavoredPaceType, "SlowPace=0");
    }

    [TestMethod]
    public void Build_JockeyChangedDetected_WhenJockeyDiffers()
    {
        // 直近騎手 j1 だが今回は j2
        var entry = MakeEntry("e1", "h1", 1, jockeyId: "j2");
        var horseHistory = new HorseRaceHistoryReadModel();
        horseHistory.SetTestData("h1");

        // LatestJockeyId を設定するために内部エントリーを追加
        // （実際はイベント駆動だが、テストでは直接セット）
        horseHistory.SetLatestJockeyId("j1");

        var input = FeatureMapper.Build(entry, horseHistory, null, 0, 0, "SlowPace", 0f, 5f);

        Assert.AreEqual(1f, input.JockeyChanged, "騎手変更あり→1");
    }

    [TestMethod]
    public void Build_JockeyNotChanged_WhenSameJockey()
    {
        var entry = MakeEntry("e1", "h1", 1, jockeyId: "j1");
        var horseHistory = new HorseRaceHistoryReadModel();
        horseHistory.SetTestData("h1");
        horseHistory.SetLatestJockeyId("j1");

        var input = FeatureMapper.Build(entry, horseHistory, null, 0, 0, "SlowPace", 0f, 5f);

        Assert.AreEqual(0f, input.JockeyChanged, "騎手継続→0");
    }

    // ------------------------------------------------------------------ //
    // helpers
    // ------------------------------------------------------------------ //

    private static RacePredictionContextEntry MakeEntry(
        string entryId, string horseId, int horseNumber,
        string? jockeyId = null,
        int? gateNumber = null,
        decimal? assignedWeight = null,
        int? age = null,
        decimal? declaredWeight = null,
        decimal? declaredWeightDiff = null,
        string? runningStyleCode = null,
        string? sexCode = null) =>
        new RacePredictionContextEntry(
            entryId, horseId, horseNumber,
            jockeyId, null, gateNumber, assignedWeight, sexCode, age,
            declaredWeight, declaredWeightDiff, runningStyleCode);
}
