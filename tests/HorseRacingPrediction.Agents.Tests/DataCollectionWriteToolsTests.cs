using HorseRacingPrediction.Agents.Plugins;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public class DataCollectionWriteToolsTests
{
    private DataCollectionWriteTools _sut = null!;
    private FakeDataCollectionWriteService _fakeService = null!;

    [TestInitialize]
    public void Setup()
    {
        _fakeService = new FakeDataCollectionWriteService();
        _sut = new DataCollectionWriteTools(_fakeService);
    }

    [TestMethod]
    public async Task UpsertHorse_CallsServiceWithCorrectName()
    {
        await _sut.UpsertHorse("イクイノックス");

        Assert.AreEqual(1, _fakeService.UpsertHorseCalls.Count, "UpsertHorseAsync が1回呼ばれること");
        Assert.AreEqual("イクイノックス", _fakeService.UpsertHorseCalls[0].RegisteredName, "馬名が正しく渡されること");
    }

    [TestMethod]
    public async Task UpsertHorse_ReturnsServiceResult()
    {
        _fakeService.HorseIdToReturn = "horse-test-id";

        var result = await _sut.UpsertHorse("イクイノックス");

        Assert.AreEqual("horse-test-id", result, "サービスが返した ID がそのまま返されること");
    }

    [TestMethod]
    public async Task UpsertRaceEntry_CallsServiceWithCorrectParameters()
    {
        const string raceId = "race-22222222-2222-2222-2222-222222222222";

        await _sut.UpsertRaceEntry(raceId, 1, "イクイノックス", "川田将雅", "木村哲也");

        Assert.AreEqual(1, _fakeService.UpsertRaceEntryCalls.Count, "UpsertRaceEntryAsync が1回呼ばれること");
        var call = _fakeService.UpsertRaceEntryCalls[0];
        Assert.AreEqual(raceId, call.RaceId);
        Assert.AreEqual(1, call.HorseNumber);
        Assert.AreEqual("イクイノックス", call.HorseName);
        Assert.AreEqual("川田将雅", call.JockeyName);
        Assert.AreEqual("木村哲也", call.TrainerName);
    }

    [TestMethod]
    public void DataCollectionWriteTools_GetAITools_HasExpectedFunctions()
    {
        var tools = _sut.GetAITools();

        Assert.IsTrue(tools.Any(tool => tool.Name == "UpsertRace"));
        Assert.IsTrue(tools.Any(tool => tool.Name == "UpsertHorse"));
        Assert.IsTrue(tools.Any(tool => tool.Name == "UpsertJockey"));
        Assert.IsTrue(tools.Any(tool => tool.Name == "UpsertTrainer"));
        Assert.IsTrue(tools.Any(tool => tool.Name == "UpsertRaceEntry"));
        Assert.IsTrue(tools.Any(tool => tool.Name == "DeclareRaceResult"));
        Assert.IsTrue(tools.Any(tool => tool.Name == "DeclareRaceEntryResult"));
        Assert.IsTrue(tools.Any(tool => tool.Name == "DeclareRacePayouts"));
    }

    // ------------------------------------------------------------------ //
    // Fake IDataCollectionWriteService
    // ------------------------------------------------------------------ //

    private sealed class FakeDataCollectionWriteService : IDataCollectionWriteService
    {
        public string HorseIdToReturn { get; set; } = "horse-fake-id";

        public record UpsertHorseCall(string RegisteredName, string? NormalizedName, string? SexCode, string? BirthDate);
        public List<UpsertHorseCall> UpsertHorseCalls { get; } = [];

        public record UpsertRaceEntryCall(string RaceId, int HorseNumber, string HorseName, string? JockeyName, string? TrainerName);
        public List<UpsertRaceEntryCall> UpsertRaceEntryCalls { get; } = [];

        public Task<string> UpsertRaceAsync(string raceDate, string racecourseCode, int raceNumber, string raceName,
            int? entryCount, string? gradeCode, string? surfaceCode, int? distanceMeters, string? directionCode,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"race-fake-{raceDate}");

        public Task<string> UpsertHorseAsync(string registeredName, string? normalizedName, string? sexCode, string? birthDate,
            CancellationToken cancellationToken = default)
        {
            UpsertHorseCalls.Add(new(registeredName, normalizedName, sexCode, birthDate));
            return Task.FromResult(HorseIdToReturn);
        }

        public Task<string> UpsertJockeyAsync(string displayName, string? normalizedName, string? affiliationCode,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"jockey-fake");

        public Task<string> UpsertTrainerAsync(string displayName, string? normalizedName, string? affiliationCode,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"trainer-fake");

        public Task<string> UpsertRaceEntryAsync(string raceId, int horseNumber, string horseName, string? jockeyName,
            string? trainerName, int? gateNumber, decimal? assignedWeight, string? sexCode, int? age,
            decimal? declaredWeight, decimal? declaredWeightDiff, CancellationToken cancellationToken = default)
        {
            UpsertRaceEntryCalls.Add(new(raceId, horseNumber, horseName, jockeyName, trainerName));
            return Task.FromResult($"レース {raceId} に馬番 {horseNumber} の出走登録を行いました。");
        }

        public Task<string> DeclareRaceResultAsync(string raceId, string winningHorseName, string? declaredAt,
            string? winningHorseId, CancellationToken cancellationToken = default)
            => Task.FromResult($"レース {raceId} の確定結果を記録しました。");

        public Task<string> DeclareRaceEntryResultAsync(string raceId, int horseNumber, int? finishPosition,
            string? officialTime, string? marginText, string? lastThreeFurlongTime, string? abnormalResultCode,
            decimal? prizeMoney, CancellationToken cancellationToken = default)
            => Task.FromResult($"レース {raceId} の馬番 {horseNumber} の成績を記録しました。");

        public Task<string> DeclareRacePayoutsAsync(string raceId, string? winPayoutsJson, string? placePayoutsJson,
            string? quinellaPayoutsJson, string? exactaPayoutsJson, string? trifectaPayoutsJson,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"レース {raceId} の払い戻しを記録しました。");
    }
}
