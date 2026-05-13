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
        var request = new RegisterHorseRequest("ディープインパクト", "deepimpact", "M", new DateOnly(2002, 3, 25), horseId);

        var response = await _client.PostAsJsonAsync("/api/horses", request, JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task RegisterHorse_ThenGetProfile_ReturnsCorrectData()
    {
        var horseId = $"horse-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest("オルフェーヴル", "orfevr", "M", null, horseId),
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
    public async Task SearchHorses_FiltersSortsAndPages()
    {
        var key = Guid.NewGuid().ToString("N");
        var horseId1 = $"horse-{Guid.NewGuid()}";
        var horseId2 = $"horse-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest($"SearchHorseA-{key}", $"searchhorse-a-{key}", "M", null, horseId1),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest($"SearchHorseB-{key}", $"searchhorse-b-{key}", "M", null, horseId2),
            JsonOptions);

        var response = await _client.GetAsync($"/api/horses?query=SearchHorse&normalizedName={key}&page=2&pageSize=1&sortBy=registeredName&sortDescending=false");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<HorseSummaryResponse>>(JsonOptions);
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.TotalCount);
        Assert.AreEqual(2, result.TotalPages);
        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual(horseId2, result.Items[0].HorseId);
    }

    [TestMethod]
    public async Task UpdateHorseProfile_AfterRegister_ReturnsOk()
    {
        var horseId = $"horse-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest("テスト馬", "testuma", null, null, horseId),
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
            "/api/horses",
            new RegisterHorseRequest("キタサンブラック", "kitasanblack", "M", null, horseId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/horses/{horseId}/aliases",
            new MergeAliasRequest("JRA", "1234567890", "JRA-DATA", true),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task CorrectHorseData_AfterRegister_ReturnsOk()
    {
        var horseId = $"horse-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/horses",
            new RegisterHorseRequest("テスト馬", "testuma", null, null, horseId),
            JsonOptions);

        var response = await _client.PatchAsJsonAsync(
            $"/api/horses/{horseId}",
            new CorrectHorseDataRequest(null, "testuma-fixed", "M", null, "データ誤り修正"),
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
