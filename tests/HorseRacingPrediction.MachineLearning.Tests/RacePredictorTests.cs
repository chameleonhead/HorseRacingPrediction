using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.MachineLearning;

namespace HorseRacingPrediction.MachineLearning.Tests;

[TestClass]
public class RacePredictorTests
{
    private RacePredictor _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _sut = new RacePredictor();
    }

    // ------------------------------------------------------------------ //
    // IsModelTrained
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void IsModelTrained_BeforeTraining_ReturnsFalse()
    {
        Assert.IsFalse(_sut.IsModelTrained);
    }

    // ------------------------------------------------------------------ //
    // PredictAsync – fallback scoring (no model)
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task PredictAsync_WithoutModel_ReturnsFallbackRanking()
    {
        var context = MakeRaceContext("race-001", 5);

        var result = await _sut.PredictAsync(context,
            (_, _) => Task.FromResult<HorseRaceHistoryReadModel?>(null),
            (_, _) => Task.FromResult<JockeyRaceHistoryReadModel?>(null));

        Assert.AreEqual("race-001", result.RaceId);
        Assert.AreEqual(5, result.Rankings.Count);
        var ranks = result.Rankings.Select(r => r.PredictedRank).OrderBy(r => r).ToList();
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, ranks);
    }

    [TestMethod]
    public async Task PredictAsync_WithHorseHistory_UsesSurfaceWinRate()
    {
        var context = MakeRaceContext("race-002", 3);

        var horseHistory = new HorseRaceHistoryReadModel();
        horseHistory.SetTestData("horse-0");
        horseHistory.AddHistoryEntry("horse-0", finishPosition: 1);
        horseHistory.AddHistoryEntry("horse-0", finishPosition: 1);
        horseHistory.AddHistoryEntry("horse-0", finishPosition: 2);

        var result = await _sut.PredictAsync(
            context,
            (horseId, _) => Task.FromResult<HorseRaceHistoryReadModel?>(horseId == "horse-0" ? horseHistory : null),
            (_, _) => Task.FromResult<JockeyRaceHistoryReadModel?>(null));

        Assert.AreEqual(3, result.Rankings.Count);
        var horse0Pred = result.Rankings.First(r => r.HorseId == "horse-0");
        Assert.AreEqual(1, horse0Pred.PredictedRank, "高勝率馬は1着予測になること");
    }

    // ------------------------------------------------------------------ //
    // TrainAsync
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task TrainAsync_WithEnoughData_SetsIsModelTrained()
    {
        var raceResults = MakeRaceResults(count: 5, entriesPerRace: 4); // 20 サンプル
        var contexts = raceResults.ToDictionary(r => r.RaceId, r => MakeRaceContext(r.RaceId, 4));

        await _sut.TrainAsync(
            raceResults,
            (raceId, _) => Task.FromResult(contexts.TryGetValue(raceId, out var ctx) ? ctx : null),
            (_, _) => Task.FromResult<HorseRaceHistoryReadModel?>(null),
            (_, _) => Task.FromResult<JockeyRaceHistoryReadModel?>(null));

        Assert.IsTrue(_sut.IsModelTrained);
    }

    [TestMethod]
    public async Task TrainAsync_ThenPredict_ReturnsSameEntryCount()
    {
        var raceResults = MakeRaceResults(count: 10, entriesPerRace: 3); // 30 サンプル
        var contexts = raceResults.ToDictionary(r => r.RaceId, r => MakeRaceContext(r.RaceId, 3));

        await _sut.TrainAsync(
            raceResults,
            (raceId, _) => Task.FromResult(contexts.TryGetValue(raceId, out var ctx) ? ctx : null),
            (_, _) => Task.FromResult<HorseRaceHistoryReadModel?>(null),
            (_, _) => Task.FromResult<JockeyRaceHistoryReadModel?>(null));

        var predRace = MakeRaceContext("race-pred", 4);
        var result = await _sut.PredictAsync(predRace,
            (_, _) => Task.FromResult<HorseRaceHistoryReadModel?>(null),
            (_, _) => Task.FromResult<JockeyRaceHistoryReadModel?>(null));

        Assert.AreEqual(4, result.Rankings.Count);
    }

    [TestMethod]
    public async Task TrainAsync_WithTooFewData_ThrowsInvalidOperation()
    {
        var raceResults = MakeRaceResults(count: 1, entriesPerRace: 2); // 2 サンプル（<10）
        var contexts = raceResults.ToDictionary(r => r.RaceId, r => MakeRaceContext(r.RaceId, 2));

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            _sut.TrainAsync(
                raceResults,
                (raceId, _) => Task.FromResult(contexts.TryGetValue(raceId, out var ctx) ? ctx : null),
                (_, _) => Task.FromResult<HorseRaceHistoryReadModel?>(null),
                (_, _) => Task.FromResult<JockeyRaceHistoryReadModel?>(null)));
    }

    // ------------------------------------------------------------------ //
    // SaveModel / LoadModel
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task SaveAndLoadModel_RoundTrips()
    {
        var raceResults = MakeRaceResults(count: 10, entriesPerRace: 3);
        var contexts = raceResults.ToDictionary(r => r.RaceId, r => MakeRaceContext(r.RaceId, 3));

        await _sut.TrainAsync(
            raceResults,
            (raceId, _) => Task.FromResult(contexts.TryGetValue(raceId, out var ctx) ? ctx : null),
            (_, _) => Task.FromResult<HorseRaceHistoryReadModel?>(null),
            (_, _) => Task.FromResult<JockeyRaceHistoryReadModel?>(null));

        using var ms = new MemoryStream();
        await _sut.SaveModelAsync(ms);

        var sut2 = new RacePredictor();
        Assert.IsFalse(sut2.IsModelTrained);

        ms.Position = 0;
        await sut2.LoadModelAsync(ms);
        Assert.IsTrue(sut2.IsModelTrained);

        var predRace = MakeRaceContext("race-pred", 3);
        var result = await sut2.PredictAsync(predRace,
            (_, _) => Task.FromResult<HorseRaceHistoryReadModel?>(null),
            (_, _) => Task.FromResult<JockeyRaceHistoryReadModel?>(null));
        Assert.AreEqual(3, result.Rankings.Count);
    }

    [TestMethod]
    public async Task SaveModelAsync_WithoutTraining_ThrowsInvalidOperation()
    {
        using var ms = new MemoryStream();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _sut.SaveModelAsync(ms));
    }

    // ------------------------------------------------------------------ //
    // Test data builders
    // ------------------------------------------------------------------ //

    private static RacePredictionContextReadModel MakeRaceContext(string raceId, int entryCount)
    {
        var model = new RacePredictionContextReadModel();
        model.SetTestData(raceId, DateOnly.Parse("2024-10-27"), "TOKYO", 11, "テストレース");

        for (var i = 0; i < entryCount; i++)
        {
            model.AddEntry(new RacePredictionContextEntry(
                $"entry-{raceId}-{i}", $"horse-{i}", i + 1,
                null, null, i + 1, 57m, "M", 4, 460m, 0m, i % 2 == 0 ? "先" : "差"));
        }

        return model;
    }

    private static List<RaceResultViewReadModel> MakeRaceResults(int count, int entriesPerRace)
    {
        var results = new List<RaceResultViewReadModel>();
        for (var r = 0; r < count; r++)
        {
            var raceId = $"race-train-{r}";
            var model = new RaceResultViewReadModel();
            model.SetTestData(raceId);

            for (var e = 0; e < entriesPerRace; e++)
            {
                model.AddEntryResult(new EntryResultSnapshot(
                    $"entry-{raceId}-{e}", $"horse-{e}", e + 1,
                    e + 1, null, null, null, null, null, null));
            }

            results.Add(model);
        }
        return results;
    }
}
