using EventFlow.Queries;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Tests;

/// <summary>
/// RaceQueryTools のモック IQueryProcessor を使ったユニットテスト。
/// </summary>
[TestClass]
public class RaceQueryToolsTests
{
    private RaceQueryTools _sut = null!;
    private FakeQueryProcessor _fakeQP = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeQP = new FakeQueryProcessor();
        _sut = new RaceQueryTools(_fakeQP);
    }

    // ------------------------------------------------------------------ //
    // GetRacePredictionContext
    // ------------------------------------------------------------------ //

    [TestMethod]
    public async Task GetRacePredictionContext_ExistingRace_ReturnsMarkdown()
    {
        _fakeQP.RaceContext = new RacePredictionContextReadModel();
        _fakeQP.RaceContext.SetTestData(
            "race-001", DateOnly.Parse("2024-10-27"), "05", 11, "天皇賞秋");

        var result = await _sut.GetRacePredictionContext("race-001");

        StringAssert.Contains(result, "race-001", "レースIDが含まれること");
        StringAssert.Contains(result, "天皇賞秋", "レース名が含まれること");
    }

    [TestMethod]
    public async Task GetRacePredictionContext_NotFound_ReturnsNotFoundMessage()
    {
        _fakeQP.RaceContext = null;

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
        _fakeQP.HorseModel = new HorseReadModel();
        _fakeQP.HorseModel.SetTestData("horse-001", "イクイノックス", "イクイノックス");

        var result = await _sut.GetHorseProfile("horse-001");

        StringAssert.Contains(result, "イクイノックス", "馬名が含まれること");
        StringAssert.Contains(result, "horse-001", "馬IDが含まれること");
    }

    [TestMethod]
    public async Task GetHorseProfile_NotFound_ReturnsNotFoundMessage()
    {
        _fakeQP.HorseModel = null;

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
        _fakeQP.JockeyModel = new JockeyReadModel();
        _fakeQP.JockeyModel.SetTestData("jockey-001", "川田将雅", "川田将雅");

        var result = await _sut.GetJockeyProfile("jockey-001");

        StringAssert.Contains(result, "川田将雅", "騎手名が含まれること");
        StringAssert.Contains(result, "jockey-001", "騎手IDが含まれること");
    }

    [TestMethod]
    public async Task GetJockeyProfile_NotFound_ReturnsNotFoundMessage()
    {
        _fakeQP.JockeyModel = null;

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
        _fakeQP.MemoBySubjectModel = null;

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
    }

    // ------------------------------------------------------------------ //
    // Fake IQueryProcessor
    // ------------------------------------------------------------------ //

    private sealed class FakeQueryProcessor : IQueryProcessor
    {
        public RacePredictionContextReadModel? RaceContext { get; set; }
        public HorseReadModel? HorseModel { get; set; }
        public JockeyReadModel? JockeyModel { get; set; }
        public MemoBySubjectReadModel? MemoBySubjectModel { get; set; }

        public Task<TResult> ProcessAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
        {
            object? result = query switch
            {
                IQuery<RacePredictionContextReadModel> => RaceContext,
                IQuery<HorseReadModel> => HorseModel,
                IQuery<JockeyReadModel> => JockeyModel,
                IQuery<MemoBySubjectReadModel> => MemoBySubjectModel,
                _ => null
            };
            return Task.FromResult((TResult)result!);
        }
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
}
