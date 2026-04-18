using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.Contracts;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public class HorseEndpointsTests
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
    public async Task RegisterHorse_ReturnsCreated()
    {
        var horseId = $"horse-{Guid.NewGuid()}";
        var request = new RegisterHorseRequest("ディープインパクト", "deepimpact", "M", new DateOnly(2002, 3, 25));

        var response = await _client.PostAsJsonAsync($"/api/horses/{horseId}", request, JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task RegisterHorse_ThenGetProfile_ReturnsCorrectData()
    {
        var horseId = $"horse-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            $"/api/horses/{horseId}",
            new RegisterHorseRequest("オルフェーヴル", "orfevr", "M", null),
            JsonOptions);

        var response = await _client.GetAsync($"/api/horses/{horseId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<HorseProfileResponse>(JsonOptions);
        Assert.IsNotNull(profile);
        Assert.AreEqual(horseId, profile.HorseId);
        Assert.AreEqual("オルフェーヴル", profile.RegisteredName);
        Assert.AreEqual("orfevr", profile.NormalizedName);
    }

    [TestMethod]
    public async Task UpdateHorseProfile_AfterRegister_ReturnsOk()
    {
        var horseId = $"horse-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            $"/api/horses/{horseId}",
            new RegisterHorseRequest("テスト馬", "testuma", null, null),
            JsonOptions);

        var response = await _client.PutAsJsonAsync(
            $"/api/horses/{horseId}",
            new UpdateHorseProfileRequest(null, "testuma-updated", "F", null),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task MergeHorseAlias_AfterRegister_ReturnsOk()
    {
        var horseId = $"horse-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            $"/api/horses/{horseId}",
            new RegisterHorseRequest("キタサンブラック", "kitasanblack", "M", null),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/horses/{horseId}/aliases",
            new MergeAliasRequest("JRA", "1234567890", "JRA-DATA", true),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
