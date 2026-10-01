using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.EventStores;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Jockeys;

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
        var request = SubjectRequestFactory.RegisterJockey("武豊", "takeyutaka", "JRA", jockeyId);

        var response = await _client.PostAsJsonAsync("/api/jockeys", request, JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task RegisterJockey_ThenGetProfile_ReturnsCorrectData()
    {
        var jockeyId = $"jockey-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/jockeys",
            SubjectRequestFactory.RegisterJockey("川田将雅", "kawadamasaya", "JRA", jockeyId),
            JsonOptions);

        var response = await _client.GetAsync($"/api/jockeys/{jockeyId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<GetJockeyProfileResponse>(JsonOptions);
        Assert.IsNotNull(profile);
        Assert.AreEqual(jockeyId, profile.Jockey.JockeyId);
        Assert.AreEqual("川田将雅", profile.Jockey.DisplayName);
        Assert.AreEqual("kawadamasaya", profile.Jockey.NormalizedName);
        Assert.AreEqual("JRA", profile.Jockey.AffiliationCode);
    }

    [TestMethod]
    public async Task SearchJockeys_FiltersSortsAndPages()
    {
        var key = Guid.NewGuid().ToString("N");
        var jockeyId1 = $"jockey-{Guid.NewGuid()}";
        var jockeyId2 = $"jockey-{Guid.NewGuid()}";

        await _client.PostAsJsonAsync(
            "/api/jockeys",
            SubjectRequestFactory.RegisterJockey($"SearchJockeyA-{key}", $"search-jockey-a-{key}", "JRA", jockeyId1),
            JsonOptions);
        await _client.PostAsJsonAsync(
            "/api/jockeys",
            SubjectRequestFactory.RegisterJockey($"SearchJockeyB-{key}", $"search-jockey-b-{key}", "JRA", jockeyId2),
            JsonOptions);

        var response = await _client.GetAsync($"/api/jockeys?query=SearchJockey&affiliationCode=JRA&page=2&pageSize=1&sortBy=displayName&sortDescending=false");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SearchJockeysResponse>(JsonOptions);
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Pagination.TotalCount);
        Assert.AreEqual(2, result.Pagination.TotalPages);
        Assert.AreEqual(1, result.Jockeys.Count);
        Assert.AreEqual(jockeyId2, result.Jockeys[0].JockeyId);
    }

    [TestMethod]
    public async Task UpdateJockeyProfile_AfterRegister_ReturnsOk()
    {
        var jockeyId = $"jockey-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/jockeys",
            SubjectRequestFactory.RegisterJockey("テスト騎手", "testjockey", null, jockeyId),
            JsonOptions);

        var response = await _client.PutAsJsonAsync(
            $"/api/jockeys/{jockeyId}",
            new UpdateJockeyProfileRequest { JockeyId = jockeyId, Jockey = new(null, null, "OVERSEAS") },
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateJockeyProfile_WhenMissing_CreatesProfile()
    {
        var jockeyId = $"jockey-{Guid.NewGuid()}";
        var response = await _client.PutAsJsonAsync($"/api/jockeys/{jockeyId}",
            new UpdateJockeyProfileRequest { JockeyId = jockeyId, Jockey = new("新規騎手", "新規騎手", "JRA") }, JsonOptions);
        var profile = await _client.GetFromJsonAsync<GetJockeyProfileResponse>($"/api/jockeys/{jockeyId}", JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(profile);
        Assert.AreEqual("新規騎手", profile.Jockey.DisplayName);
    }

    [TestMethod]
    public async Task UpdateJockeyProfile_ConcurrentReplay_AddsOneEvent()
    {
        var jockeyId = $"jockey-{Guid.NewGuid()}";
        var request = new UpdateJockeyProfileRequest { JockeyId = jockeyId, Jockey = new("同時騎手", "同時騎手", "JRA") };

        var responses = await Task.WhenAll(
            _client.PutAsJsonAsync($"/api/jockeys/{jockeyId}", request, JsonOptions),
            _client.PutAsJsonAsync($"/api/jockeys/{jockeyId}", request, JsonOptions));
        await _client.PutAsJsonAsync($"/api/jockeys/{jockeyId}", request, JsonOptions);

        Assert.IsTrue(responses.All(response => response.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, CountSubjectEvents(jockeyId));
    }

    private static int CountSubjectEvents(string aggregateId)
    {
        var provider = _app.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>();
        using var db = provider.CreateContext();
        return db.Set<EventEntity>().Count(item => item.AggregateId == aggregateId);
    }

    [TestMethod]
    public async Task MergeJockeyAlias_AfterRegister_ReturnsOk()
    {
        var jockeyId = $"jockey-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/jockeys",
            SubjectRequestFactory.RegisterJockey("福永祐一", "fukunagayuichi", "JRA", jockeyId),
            JsonOptions);

        var response = await _client.PostAsJsonAsync(
            $"/api/jockeys/{jockeyId}/aliases",
            new MergeJockeyAliasRequest { JockeyId = jockeyId, Alias = new("JRA", "J00123", "JRA-DATA", true) },
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task CorrectJockeyData_AfterRegister_ReturnsOk()
    {
        var jockeyId = $"jockey-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync(
            "/api/jockeys",
            SubjectRequestFactory.RegisterJockey("テスト騎手", "testjockey", null, jockeyId),
            JsonOptions);

        var response = await _client.PatchAsJsonAsync(
            $"/api/jockeys/{jockeyId}",
            new CorrectJockeyDataRequest { JockeyId = jockeyId, Jockey = new(null, "testjockey-fixed", "JRA", "名前誤り修正") },
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
