using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.Contracts;
using Microsoft.AspNetCore.TestHost;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public class AuthenticationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static WebApplication _app = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        (_app, _) = await TestApplicationFactory.CreateAsync();
    }

    [ClassCleanup]
    public static async Task ClassClean()
    {
        await _app.DisposeAsync();
    }

    private HttpClient CreateClient()
    {
        return _app.GetTestClient();
    }

    [TestMethod]
    public async Task Post_WithoutApiKey_ReturnsUnauthorized()
    {
        using var client = CreateClient();
        var raceId = $"race-{Guid.NewGuid()}";

        var response = await client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Post_WithWrongApiKey_ReturnsUnauthorized()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");
        var raceId = $"race-{Guid.NewGuid()}";

        var response = await client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Post_WithCorrectApiKey_Succeeds()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var raceId = $"race-{Guid.NewGuid()}";

        var response = await client.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task Get_WithoutApiKey_ReturnsOk()
    {
        // First create a race with API key
        using var authClient = CreateClient();
        authClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var raceId = $"race-{Guid.NewGuid()}";
        await authClient.PostAsJsonAsync(
            "/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞", raceId),
            JsonOptions);

        // Then GET without API key
        using var unauthClient = CreateClient();
        var response = await unauthClient.GetAsync($"/api/races/{raceId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task HealthEndpoint_ReturnsOk()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
