using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Agents.Contracts;
using HorseRacingPrediction.Api.Contracts;

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
    public async Task CreateRace_WhenAlreadyExists_ReturnsConflict()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var request = new CreateRaceRequest(
            new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId);

        var firstResponse = await _client.PostAsJsonAsync("/api/races", request, JsonOptions);
        var secondResponse = await _client.PostAsJsonAsync("/api/races", request, JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, secondResponse.StatusCode);
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
    public async Task SearchRaces_FiltersSortsAndPages()
    {
        var key = Guid.NewGuid().ToString("N");
        var tokyoRace1 = $"race-{Guid.NewGuid()}";
        var tokyoRace2 = $"race-{Guid.NewGuid()}";
        var nakayamaRace = $"race-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 3, $"RaceSearch-{key}-A", tokyoRace1),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 7, $"RaceSearch-{key}-B", tokyoRace2),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "NAKAYAMA", 11, $"RaceSearch-{key}-C", nakayamaRace),
            JsonOptions);

        var response = await _client.GetAsync($"/api/races?racecourseCode=TOKYO&raceName=RaceSearch-{key}&page=2&pageSize=1&sortBy=raceNumber&sortDescending=false");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<RaceSummaryResponse>>(JsonOptions);
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.TotalCount);
        Assert.AreEqual(2, result.TotalPages);
        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(tokyoRace2, result.Items[0].RaceId);
        Assert.AreEqual(7, result.Items[0].RaceNumber);
    }

    [TestMethod]
    public async Task SearchRaces_AfterAppRestart_UsesPersistedRaceSummaryReadModel()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"race-summary-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        var raceId = $"race-{Guid.NewGuid()}";

        try
        {
            var (firstApp, firstClient) = await TestApplicationFactory.CreateAsync(connectionString);
            firstClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

            var createResponse = await firstClient.PostAsJsonAsync(
                "/api/races",
                new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 9, "Restart Persistence Cup", raceId),
                JsonOptions);

            Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);

            firstClient.Dispose();
            await firstApp.DisposeAsync();

            var (secondApp, secondClient) = await TestApplicationFactory.CreateAsync(connectionString);
            secondClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

            try
            {
                var getResponse = await secondClient.GetAsync($"/api/races/{raceId}");
                Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);

                var race = await getResponse.Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);
                Assert.IsNotNull(race);
                Assert.AreEqual(raceId, race.RaceId);

                var searchResponse = await secondClient.GetAsync($"/api/races?raceId={raceId}");
                Assert.AreEqual(HttpStatusCode.OK, searchResponse.StatusCode);

                var result = await searchResponse.Content.ReadFromJsonAsync<PagedResponse<RaceSummaryResponse>>(JsonOptions);
                Assert.IsNotNull(result);
                Assert.AreEqual(1, result.TotalCount);
                Assert.AreEqual(raceId, result.Items[0].RaceId);
            }
            finally
            {
                secondClient.Dispose();
                await secondApp.DisposeAsync();
            }
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
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
    public async Task PublishCard_WhenAlreadyPublished_ReturnsConflict()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId),
            JsonOptions);

        var firstResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(18),
            JsonOptions);
        var secondResponse = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(18),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, secondResponse.StatusCode);
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
    public async Task DeclareResult_BeforePublishCard_ReturnsConflict()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("ディープインパクト", DateTimeOffset.UtcNow),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
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
    public async Task GetRace_AfterFullLifecycle_ReturnsCardWeatherTrackAndResultDetails()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var entryId = $"entry-{Guid.NewGuid()}";
        var horseId = $"horse-{Guid.NewGuid()}";
        var observedAt = DateTimeOffset.UtcNow;

        await _client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 12, 28), "NAKAYAMA", 11, "有馬記念", raceId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(16),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horseId, 1, null, null, 1, 57.0m, "M", 4, 450.0m, 0.0m, null, entryId),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/weather",
            new RecordWeatherObservationRequest(observedAt, "SUNNY", "晴れ", 22.5m, 55.0m, "N", 3.2m),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/track-condition",
            new RecordTrackConditionRequest(observedAt, "GOOD", "STANDARD", "良"),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("イクイノックス", observedAt),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries/{entryId}/result",
            new DeclareEntryResultRequest(1, "2:11.3", null, "35.1", null, 500000m),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/payout",
            new DeclarePayoutResultRequest(
                observedAt,
                WinPayouts: [new PayoutEntryDto("1", 350m)],
                PlacePayouts: [new PayoutEntryDto("1", 180m)],
                QuinellaPayouts: null,
                ExactaPayouts: null,
                TrifectaPayouts: null),
            JsonOptions);

        var response = await _client.GetAsync($"/api/races/{raceId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var race = await response.Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);
        Assert.IsNotNull(race);
        Assert.AreEqual(1, race.Entries.Count);
        Assert.AreEqual(entryId, race.Entries[0].EntryId);
        Assert.AreEqual(1, race.WeatherObservations.Count);
        Assert.AreEqual("晴れ", race.WeatherObservations[0].WeatherText);
        Assert.AreEqual(1, race.TrackConditionObservations.Count);
        Assert.AreEqual("良", race.TrackConditionObservations[0].GoingDescriptionText);
        Assert.AreEqual("イクイノックス", race.WinningHorseName);
        Assert.IsNull(race.WinningHorseId);
        Assert.IsNull(race.StewardReportText);
        Assert.AreEqual(1, race.EntryResults.Count);
        Assert.AreEqual(1, race.EntryResults[0].FinishPosition);
        Assert.IsNotNull(race.PayoutResult);
        Assert.AreEqual(1, race.PayoutResult.WinPayouts.Count);
        Assert.AreEqual(350m, race.PayoutResult.WinPayouts[0].Amount);
        Assert.IsNotNull(race.Odds);
        Assert.IsFalse(race.Odds.IsAvailable);
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
