using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// RaceQueryTools の FakeRaceQueryService を使ったユニットテスト。
/// </summary>
[TestClass]
public class RaceQueryToolsTests
{
    private RaceQueryTools _sut = null!;
    private FakeRaceQueryService _fakeService = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeService = new FakeRaceQueryService();
        _sut = new RaceQueryTools(_fakeService);
    }

    // ------------------------------------------------------------------ //
    // GetRacePredictionContext
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task GetRacePredictionContext_ExistingRace_ReturnsMarkdown()
    {
        _fakeService.RaceContext = new RacePredictionContextReadModel();
        _fakeService.RaceContext.SetTestData(
            "race-001", DateOnly.Parse("2024-10-27"), "05", 11, "天皇賞秋");

        var result = await _sut.GetRacePredictionContext("race-001");

        StringAssert.Contains(result, "race-001", "レースIDが含まれること");
        StringAssert.Contains(result, "天皇賞秋", "レース名が含まれること");
    }

    [TestMethod]
    public async Task GetRacePredictionContext_NotFound_ReturnsNotFoundMessage()
    {
        _fakeService.RaceContext = null;

        var result = await _sut.GetRacePredictionContext("race-999");

        StringAssert.Contains(result, "race-999", "検索IDが含まれること");
        StringAssert.Contains(result, "見つかりませんでした", "見つからないメッセージが含まれること");
    }

    // ------------------------------------------------------------------ //
    // GetHorseProfile
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task GetHorseProfile_ExistingHorse_ReturnsMarkdown()
    {
        _fakeService.HorseModel = new HorseReadModel();
        _fakeService.HorseModel.SetTestData("horse-001", "イクイノックス", "イクイノックス");

        var result = await _sut.GetHorseProfile("horse-001");

        StringAssert.Contains(result, "イクイノックス", "馬名が含まれること");
        StringAssert.Contains(result, "horse-001", "馬IDが含まれること");
    }

    [TestMethod]
    public async Task GetHorseProfile_NotFound_ReturnsNotFoundMessage()
    {
        _fakeService.HorseModel = null;

        var result = await _sut.GetHorseProfile("horse-999");

        StringAssert.Contains(result, "horse-999", "検索IDが含まれること");
        StringAssert.Contains(result, "見つかりませんでした", "見つからないメッセージが含まれること");
    }

    // ------------------------------------------------------------------ //
    // GetJockeyProfile
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task GetJockeyProfile_ExistingJockey_ReturnsMarkdown()
    {
        _fakeService.JockeyModel = new JockeyReadModel();
        _fakeService.JockeyModel.SetTestData("jockey-001", "川田将雅", "川田将雅");

        var result = await _sut.GetJockeyProfile("jockey-001");

        StringAssert.Contains(result, "川田将雅", "騎手名が含まれること");
        StringAssert.Contains(result, "jockey-001", "騎手IDが含まれること");
    }

    [TestMethod]
    public async Task GetJockeyProfile_NotFound_ReturnsNotFoundMessage()
    {
        _fakeService.JockeyModel = null;

        var result = await _sut.GetJockeyProfile("jockey-999");

        StringAssert.Contains(result, "jockey-999", "検索IDが含まれること");
        StringAssert.Contains(result, "見つかりませんでした", "見つからないメッセージが含まれること");
    }

    // ------------------------------------------------------------------ //
    // GetMemosBySubject – no memos case
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task GetMemosBySubject_NoMemos_ReturnsEmptyMessage()
    {
        _fakeService.MemoBySubjectModel = null;

        var result = await _sut.GetMemosBySubject("Horse", "horse-001");

        StringAssert.Contains(result, "horse-001", "対象IDが含まれること");
        StringAssert.Contains(result, "ありません", "メモなしメッセージが含まれること");
    }

    // ------------------------------------------------------------------ //
    // GetAITools registration
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void RaceQueryTools_GetAITools_HasExpectedFunctions()
    {
        var tools = _sut.GetAITools();

        Assert.IsTrue(tools.Any(t => t.Name == "GetRacePredictionContext"), "GetRacePredictionContext が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "GetHorseProfile"), "GetHorseProfile が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "GetJockeyProfile"), "GetJockeyProfile が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "GetMemosBySubject"), "GetMemosBySubject が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "GetHorseRaceStats"), "GetHorseRaceStats が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "GetJockeyRaceStats"), "GetJockeyRaceStats が登録されていること");
        Assert.IsTrue(tools.Any(t => t.Name == "GetRaceFieldAnalysis"), "GetRaceFieldAnalysis が登録されていること");
    }

    // ------------------------------------------------------------------ //
    // GetHorseRaceStats
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task GetHorseRaceStats_ExistingHorse_ReturnsMarkdown()
    {
        var model = new HorseRaceHistoryReadModel();
        model.SetTestData("horse-001");
        _fakeService.HorseHistoryModel = model;

        var result = await _sut.GetHorseRaceStats("horse-001");

        StringAssert.Contains(result, "horse-001", "馬IDが含まれること");
        StringAssert.Contains(result, "勝率", "勝率が含まれること");
    }

    [TestMethod]
    public async Task GetHorseRaceStats_NotFound_ReturnsNotFoundMessage()
    {
        _fakeService.HorseHistoryModel = null;

        var result = await _sut.GetHorseRaceStats("horse-999");

        StringAssert.Contains(result, "horse-999", "検索IDが含まれること");
        StringAssert.Contains(result, "ありません", "履歴なしメッセージが含まれること");
    }

    // ------------------------------------------------------------------ //
    // GetJockeyRaceStats
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task GetJockeyRaceStats_ExistingJockey_ReturnsMarkdown()
    {
        var model = new JockeyRaceHistoryReadModel();
        model.SetTestData("jockey-001");
        _fakeService.JockeyHistoryModel = model;

        var result = await _sut.GetJockeyRaceStats("jockey-001");

        StringAssert.Contains(result, "jockey-001", "騎手IDが含まれること");
        StringAssert.Contains(result, "勝率", "勝率が含まれること");
    }

    [TestMethod]
    public async Task GetJockeyRaceStats_NotFound_ReturnsNotFoundMessage()
    {
        _fakeService.JockeyHistoryModel = null;

        var result = await _sut.GetJockeyRaceStats("jockey-999");

        StringAssert.Contains(result, "jockey-999", "検索IDが含まれること");
        StringAssert.Contains(result, "ありません", "履歴なしメッセージが含まれること");
    }

    // ------------------------------------------------------------------ //
    // GetRaceFieldAnalysis
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task GetRaceFieldAnalysis_ExistingRace_ReturnsMarkdown()
    {
        _fakeService.RaceContext = new RacePredictionContextReadModel();
        _fakeService.RaceContext.SetTestData("race-001", DateOnly.Parse("2024-10-27"), "05", 11, "天皇賞秋");

        var result = await _sut.GetRaceFieldAnalysis("race-001");

        StringAssert.Contains(result, "race-001", "レースIDが含まれること");
        StringAssert.Contains(result, "FieldLeaderCount", "逃げ馬頭数が含まれること");
        StringAssert.Contains(result, "FavoredPaceType", "ペースタイプが含まれること");
    }

    [TestMethod]
    public async Task GetRaceFieldAnalysis_NotFound_ReturnsNotFoundMessage()
    {
        _fakeService.RaceContext = null;

        var result = await _sut.GetRaceFieldAnalysis("race-999");

        StringAssert.Contains(result, "race-999", "検索IDが含まれること");
        StringAssert.Contains(result, "見つかりませんでした", "見つからないメッセージが含まれること");
    }

    // ------------------------------------------------------------------ //
    // Fake IRaceQueryService
    // ------------------------------------------------------------------ //

    private sealed class FakeRaceQueryService : IRaceQueryService
    {
        public RacePredictionContextReadModel? RaceContext { get; set; }
        public HorseReadModel? HorseModel { get; set; }
        public JockeyReadModel? JockeyModel { get; set; }
        public MemoBySubjectReadModel? MemoBySubjectModel { get; set; }
        public HorseRaceHistoryReadModel? HorseHistoryModel { get; set; }
        public JockeyRaceHistoryReadModel? JockeyHistoryModel { get; set; }

        public Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(string raceId, CancellationToken cancellationToken = default)
            => Task.FromResult(RaceContext);

        public Task<HorseReadModel?> GetHorseAsync(string horseId, CancellationToken cancellationToken = default)
            => Task.FromResult(HorseModel);

        public Task<JockeyReadModel?> GetJockeyAsync(string jockeyId, CancellationToken cancellationToken = default)
            => Task.FromResult(JockeyModel);

        public Task<MemoBySubjectReadModel?> GetMemosBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default)
            => Task.FromResult(MemoBySubjectModel);

        public Task<HorseRaceHistoryReadModel?> GetHorseRaceHistoryAsync(string horseId, CancellationToken cancellationToken = default)
            => Task.FromResult(HorseHistoryModel);

        public Task<JockeyRaceHistoryReadModel?> GetJockeyRaceHistoryAsync(string jockeyId, CancellationToken cancellationToken = default)
            => Task.FromResult(JockeyHistoryModel);
    }
}

/// <summary>
/// テスト用の ReadModel ヘルパー拡張メソッド
/// </summary>
internal static class ReadModelTestExtensions
{
    public static void SetTestData(
        this RacePredictionContextReadModel model,
        string raceId, DateOnly raceDate, string racecourseCode, int raceNumber, string raceName)
    {
        // リフレクションでプロパティをセット（テスト用）
        typeof(RacePredictionContextReadModel)
            .GetProperty(nameof(RacePredictionContextReadModel.RaceId))!
            .SetValue(model, raceId);
        typeof(RacePredictionContextReadModel)
            .GetProperty(nameof(RacePredictionContextReadModel.RaceDate))!
            .SetValue(model, raceDate);
        typeof(RacePredictionContextReadModel)
            .GetProperty(nameof(RacePredictionContextReadModel.RacecourseCode))!
            .SetValue(model, racecourseCode);
        typeof(RacePredictionContextReadModel)
            .GetProperty(nameof(RacePredictionContextReadModel.RaceNumber))!
            .SetValue(model, raceNumber);
        typeof(RacePredictionContextReadModel)
            .GetProperty(nameof(RacePredictionContextReadModel.RaceName))!
            .SetValue(model, raceName);
    }

    public static void SetTestData(
        this HorseReadModel model,
        string horseId, string registeredName, string normalizedName)
    {
        typeof(HorseReadModel)
            .GetProperty(nameof(HorseReadModel.HorseId))!
            .SetValue(model, horseId);
        typeof(HorseReadModel)
            .GetProperty(nameof(HorseReadModel.RegisteredName))!
            .SetValue(model, registeredName);
        typeof(HorseReadModel)
            .GetProperty(nameof(HorseReadModel.NormalizedName))!
            .SetValue(model, normalizedName);
    }

    public static void SetTestData(
        this JockeyReadModel model,
        string jockeyId, string displayName, string normalizedName)
    {
        typeof(JockeyReadModel)
            .GetProperty(nameof(JockeyReadModel.JockeyId))!
            .SetValue(model, jockeyId);
        typeof(JockeyReadModel)
            .GetProperty(nameof(JockeyReadModel.DisplayName))!
            .SetValue(model, displayName);
        typeof(JockeyReadModel)
            .GetProperty(nameof(JockeyReadModel.NormalizedName))!
            .SetValue(model, normalizedName);
    }

    public static void SetTestData(
        this TrainerReadModel model,
        string trainerId, string displayName, string normalizedName)
    {
        typeof(TrainerReadModel)
            .GetProperty(nameof(TrainerReadModel.TrainerId))!
            .SetValue(model, trainerId);
        typeof(TrainerReadModel)
            .GetProperty(nameof(TrainerReadModel.DisplayName))!
            .SetValue(model, displayName);
        typeof(TrainerReadModel)
            .GetProperty(nameof(TrainerReadModel.NormalizedName))!
            .SetValue(model, normalizedName);
    }

    public static void SetTestData(
        this HorseRaceHistoryReadModel model,
        string horseId)
    {
        typeof(HorseRaceHistoryReadModel)
            .GetProperty(nameof(HorseRaceHistoryReadModel.HorseId))!
            .SetValue(model, horseId);
    }

    public static void SetTestData(
        this JockeyRaceHistoryReadModel model,
        string jockeyId)
    {
        typeof(JockeyRaceHistoryReadModel)
            .GetProperty(nameof(JockeyRaceHistoryReadModel.JockeyId))!
            .SetValue(model, jockeyId);
    }
}
