using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.Contracts;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public class JockeyEndpointsTests
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
    public async Task RegisterJockey_ReturnsCreated()
    {
        var jockeyId = $"jockey-{Guid.NewGuid()}";
        var request = new RegisterJockeyRequest("武豊", "takeyutaka", "JRA", jockeyId);

        var response = await _client.PostAsJsonAsync("/api/jockeys", request, JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task RegisterJockey_ThenGetProfile_ReturnsCorrectData()
    {
        var jockeyId = $"jockey-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/jockeys",
            new RegisterJockeyRequest("川田将雅", "kawadamasaya", "JRA", jockeyId),
            JsonOptions);

        var response = await _client.GetAsync($"/api/jockeys/{jockeyId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<JockeyProfileResponse>(JsonOptions);
        Assert.IsNotNull(profile);
        Assert.AreEqual(jockeyId, profile.JockeyId);
        Assert.AreEqual("川田将雅", profile.DisplayName);
        Assert.AreEqual("kawadamasaya", profile.NormalizedName);
        Assert.AreEqual("JRA", profile.AffiliationCode);
    }

    [TestMethod]
    public async Task UpdateJockeyProfile_AfterRegister_ReturnsOk()
    {
        var jockeyId = $"jockey-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/jockeys",
            new RegisterJockeyRequest("テスト騎手", "testjockey", null, jockeyId),
            JsonOptions);

        var response = await _client.PutAsJsonAsync(
            $"/api/jockeys/{jockeyId}",
            new UpdateJockeyProfileRequest(null, null, "OVERSEAS"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task MergeJockeyAlias_AfterRegister_ReturnsOk()
    {
        var jockeyId = $"jockey-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/jockeys",
            new RegisterJockeyRequest("福永祐一", "fukunagayuichi", "JRA", jockeyId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/jockeys/{jockeyId}/aliases",
            new MergeAliasRequest("JRA", "J00123", "JRA-DATA", true),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task CorrectJockeyData_AfterRegister_ReturnsOk()
    {
        var jockeyId = $"jockey-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/jockeys",
            new RegisterJockeyRequest("テスト騎手", "testjockey", null, jockeyId),
            JsonOptions);

        var response = await _client.PatchAsJsonAsync(
            $"/api/jockeys/{jockeyId}",
            new CorrectJockeyDataRequest(null, "testjockey-fixed", "JRA", "名前誤り修正"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
