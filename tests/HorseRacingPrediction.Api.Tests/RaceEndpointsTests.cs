using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public class RaceEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static WebApplication _app = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        (_app, _client) = await TestApplicationFactory.CreateAsync();
        _client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
    }

    [ClassCleanup]
    public static async Task ClassClean()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [TestMethod]
    public async Task CreateRace_ReturnsCreated()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var request = new CreateRaceRequest(
            new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId);

        var response = await _client.PostAsJsonAsync("/api/races", request, JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.IsTrue(response.Headers.Location?.ToString().Contains($"/api/races/{raceId}"));
    }

    [TestMethod]
    public async Task GetRace_AfterCreate_ReturnsCorrectData()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var request = new CreateRaceRequest(
            new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId);
        await _client.PostAsJsonAsync("/api/races", request, JsonOptions);

        var response = await _client.GetAsync($"/api/races/{raceId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var race = await response.Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);
        Assert.IsNotNull(race);
        Assert.AreEqual(raceId, race.RaceId);
        Assert.AreEqual("TOKYO", race.RacecourseCode);
        Assert.AreEqual(5, race.RaceNumber);
        Assert.AreEqual("皐月賞", race.RaceName);
        Assert.AreEqual(RaceStatus.Draft, race.Status);
    }

    [TestMethod]
    public async Task PublishCard_AfterCreate_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(18),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task DeclareResult_AfterPublishCard_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(18),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("ディープインパクト", DateTimeOffset.UtcNow),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task FullLifecycle_WithEntryAndPayout_ProducesCorrectState()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var entryId = $"entry-{Guid.NewGuid()}";
        var horseId = $"horse-{Guid.NewGuid()}";
        var declaredAt = DateTimeOffset.UtcNow;

        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 12, 28), "NAKAYAMA", 11, "有馬記念", raceId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(16),
            JsonOptions);

        var entryResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horseId, 1, null, null, 1, 57.0m, "M", 4, 450.0m, 0.0m, entryId),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, entryResponse.StatusCode);

        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("イクイノックス", declaredAt),
            JsonOptions);

        var entryResultResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries/{entryId}/result",
            new DeclareEntryResultRequest(1, "2:11.3", null, "35.1", null, null),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, entryResultResponse.StatusCode);

        var payoutResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/payout",
            new DeclarePayoutResultRequest(
                declaredAt,
                WinPayouts: new[] { new PayoutEntryDto("1", 350m) },
                PlacePayouts: null,
                QuinellaPayouts: null,
                ExactaPayouts: null,
                TrifectaPayouts: null),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, payoutResponse.StatusCode);

        var closeResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/close",
            (object?)null,
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, closeResponse.StatusCode);
    }

    [TestMethod]
    public async Task RecordWeatherObservation_AfterCreate_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "東京優駿", raceId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/weather",
            new RecordWeatherObservationRequest(DateTimeOffset.UtcNow, "SUNNY", "晴れ", 22.5m, 55.0m, "N", 3.2m),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task RecordTrackCondition_AfterCreate_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "東京優駿", raceId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/track-condition",
            new RecordTrackConditionRequest(DateTimeOffset.UtcNow, "GOOD", null, "Good to Firm"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task OpenPreRaceAndStartRace_AfterPublishCard_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "東京優駿", raceId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(18),
            JsonOptions);

        var openResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/open-pre-race",
            (object?)null,
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, openResponse.StatusCode);

        var startResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/start",
            (object?)null,
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, startResponse.StatusCode);
    }

    [TestMethod]
    public async Task CorrectRaceData_AfterCreate_ReturnsOk()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "誤ったレース名", raceId),
            JsonOptions);

        var response = await _client.PatchAsJsonAsync(
            $"/api/races/{raceId}",
            new CorrectRaceDataRequest("正しいレース名", null, null, "G1", "TURF", 2400, null, "レース名の修正"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
