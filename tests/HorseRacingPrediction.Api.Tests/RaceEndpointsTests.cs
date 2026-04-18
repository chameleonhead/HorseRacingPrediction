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
            new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        var response = await _client.PostAsJsonAsync($"/api/races/{raceId}", request, JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.IsTrue(response.Headers.Location?.ToString().Contains($"/api/races/{raceId}"));
    }

    [TestMethod]
    public async Task GetRace_AfterCreate_ReturnsCorrectData()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var request = new CreateRaceRequest(
            new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        await _client.PostAsJsonAsync($"/api/races/{raceId}", request, JsonOptions);

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
            $"/api/races/{raceId}",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"),
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
            $"/api/races/{raceId}",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞"),
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
    public async Task FullLifecycle_ProducesCorrectState()
    {
        var raceId = $"race-{Guid.NewGuid()}";
        var declaredAt = DateTimeOffset.UtcNow;

        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}",
            new CreateRaceRequest(new DateOnly(2025, 12, 28), "NAKAYAMA", 11, "有馬記念"),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(16),
            JsonOptions);
        await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("イクイノックス", declaredAt),
            JsonOptions);

        var response = await _client.GetAsync($"/api/races/{raceId}");
        var race = await response.Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);

        Assert.IsNotNull(race);
        Assert.AreEqual("有馬記念", race.RaceName);
        Assert.AreEqual(RaceStatus.ResultDeclared, race.Status);
        Assert.AreEqual(16, race.EntryCount);
        Assert.AreEqual("イクイノックス", race.WinningHorseName);
    }
}
