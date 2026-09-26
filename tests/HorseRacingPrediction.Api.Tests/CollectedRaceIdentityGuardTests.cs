using System.Net;
using System.Net.Http.Json;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.EventStores;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectedRaceIdentityGuardTests
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExistingHorseNumberCannotBeReassigned_WhenOfficialIdentitiesAreSwapped(bool refresh)
    {
        var (app, client) = await CreateApplicationAsync();
        await using var application = app;
        using var http = client;

        var date = new DateOnly(2036, 9, 19);
        const string course = "東京";
        const int raceNumber = 7;
        var first = new DeclareRaceResultBulkRequest(date, course, raceNumber, "主体固定検証",
            EntryCount: 2, IsRaceCard: true,
            Entries:
            [
                Entry(1, "固定馬A", SourceIdentity("100001")),
                Entry(2, "固定馬B", SourceIdentity("100002")),
            ]);
        var initialResponse = await http.PostAsJsonAsync("/api/races/result-bulk", first, JsonOptions);
        var initialBody = await ReadBodyAsync(initialResponse);
        Assert.AreEqual(HttpStatusCode.OK, initialResponse.StatusCode);
        Assert.IsTrue(initialBody.CorePersisted);

        var beforeContext = await GetContextJsonAsync(http, initialBody.RaceId);
        var eventsBefore = CountStoredEvents(app);
        var tasksBefore = await CountTasksAsync(app);
        var swapped = first with
        {
            TargetRaceId = refresh ? initialBody.RaceId : null,
            RefreshExistingData = refresh,
            Entries =
            [
                Entry(1, "固定馬B", SourceIdentity("100002")),
                Entry(2, "固定馬A", SourceIdentity("100001")),
            ],
        };

        var rejectedResponse = await http.PostAsJsonAsync("/api/races/result-bulk", swapped, JsonOptions);
        var rejected = await ReadBodyAsync(rejectedResponse);
        Assert.AreEqual(HttpStatusCode.OK, rejectedResponse.StatusCode);
        Assert.IsFalse(rejected.CorePersisted);
        Assert.IsTrue(rejected.Outcomes!.Any(x => x.ErrorCode == "RaceEntryIdentityMismatch"));
        Assert.AreEqual(eventsBefore, CountStoredEvents(app));
        Assert.AreEqual(tasksBefore, await CountTasksAsync(app));
        Assert.AreEqual(beforeContext, await GetContextJsonAsync(http, initialBody.RaceId));

        var replayResponse = await http.PostAsJsonAsync("/api/races/result-bulk", first, JsonOptions);
        var replay = await ReadBodyAsync(replayResponse);
        Assert.AreEqual(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.IsTrue(replay.CorePersisted);
        Assert.IsEmpty(replay.Errors, string.Join(" | ", replay.Errors));
        Assert.AreEqual(eventsBefore, CountStoredEvents(app));
    }

    [TestMethod]
    public async Task ExistingHorseRejectsSameNameWhenOfficialSourceIdentityChanges()
    {
        var (app, client) = await CreateApplicationAsync();
        await using var application = app;
        using var http = client;
        var date = new DateOnly(2036, 9, 20);
        var course = $"IDENTITY-NAME-{Guid.NewGuid():N}";
        var first = new DeclareRaceResultBulkRequest(date, course, 8, "主体名一致検証",
            EntryCount: 1, IsRaceCard: true,
            Entries: [Entry(1, "同名馬", SourceIdentity("110001"))]);
        var initial = await http.PostAsJsonAsync("/api/races/result-bulk", first, JsonOptions);
        var initialBody = await ReadBodyAsync(initial);
        var beforeContext = await GetContextJsonAsync(http, initialBody.RaceId);
        var eventsBefore = CountStoredEvents(app);
        var tasksBefore = await CountTasksAsync(app);

        var changedSource = first with
        {
            Entries = [Entry(1, "同名馬", SourceIdentity("110002"))],
        };
        var response = await http.PostAsJsonAsync("/api/races/result-bulk", changedSource, JsonOptions);
        var body = await ReadBodyAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(body.CorePersisted);
        Assert.IsTrue(body.Outcomes!.Any(x => x.ErrorCode == "RaceEntryIdentityMismatch"));
        Assert.AreEqual(eventsBefore, CountStoredEvents(app));
        Assert.AreEqual(tasksBefore, await CountTasksAsync(app));
        Assert.AreEqual(beforeContext, await GetContextJsonAsync(http, initialBody.RaceId));
    }

    [TestMethod]
    public async Task InvalidHorseSourceIdentityIsRejectedBeforeAnyRelatedWrite()
    {
        var (app, client) = await CreateApplicationAsync();
        await using var application = app;
        using var http = client;
        var request = new DeclareRaceResultBulkRequest(new DateOnly(2036, 9, 21),
            $"IDENTITY-INVALID-{Guid.NewGuid():N}", 9, "不正主体検証", EntryCount: 1, IsRaceCard: true,
            Entries: [Entry(1, "不正主体馬", "https://example.invalid/not-a-jra-identity")]);
        var eventsBefore = CountStoredEvents(app);
        var tasksBefore = await CountTasksAsync(app);

        var response = await http.PostAsJsonAsync("/api/races/result-bulk", request, JsonOptions);
        var body = await ReadBodyAsync(response);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(body.CorePersisted);
        Assert.IsTrue(body.Outcomes!.Any(x => x.ErrorCode == "InvalidHorseSourceIdentity"));
        Assert.AreEqual(eventsBefore, CountStoredEvents(app));
        Assert.AreEqual(tasksBefore, await CountTasksAsync(app));
    }

    [TestMethod]
    public async Task ConflictingExistingHorseRejectsWholeEnvelopeBeforeNewHorseIsMaterialized()
    {
        var (app, client) = await CreateApplicationAsync();
        await using var application = app;
        using var http = client;
        var date = new DateOnly(2036, 9, 22);
        var course = $"IDENTITY-PREFLIGHT-{Guid.NewGuid():N}";
        var first = new DeclareRaceResultBulkRequest(date, course, 10, "全体事前判定",
            EntryCount: 1, IsRaceCard: true,
            Entries: [Entry(1, "既存主体馬", SourceIdentity("120001"))]);
        var initial = await http.PostAsJsonAsync("/api/races/result-bulk", first, JsonOptions);
        var initialBody = await ReadBodyAsync(initial);
        var beforeContext = await GetContextJsonAsync(http, initialBody.RaceId);
        var eventsBefore = CountStoredEvents(app);
        var tasksBefore = await CountTasksAsync(app);
        var newHorse = "事前判定新規馬";
        var conflicting = first with
        {
            Entries =
            [
                Entry(2, newHorse, SourceIdentity("120002")),
                Entry(1, "既存主体馬", SourceIdentity("120003")),
            ],
        };

        var response = await http.PostAsJsonAsync("/api/races/result-bulk", conflicting, JsonOptions);
        var body = await ReadBodyAsync(response);
        var newHorseId = HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildHorseId(
            newHorse, SourceIdentity("120002"));
        var newHorseResponse = await http.GetAsync($"/api/horses/{newHorseId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(body.CorePersisted);
        Assert.IsTrue(body.Outcomes!.Any(x => x.ErrorCode == "RaceEntryIdentityMismatch"));
        Assert.AreEqual(eventsBefore, CountStoredEvents(app));
        Assert.AreEqual(tasksBefore, await CountTasksAsync(app));
        Assert.AreEqual(beforeContext, await GetContextJsonAsync(http, initialBody.RaceId));
        Assert.AreEqual(HttpStatusCode.NotFound, newHorseResponse.StatusCode);
    }

    private static RaceResultEntryBulkDto Entry(int number, string name, string sourceIdentity) =>
        new(number, null, null, null, null, null, null, HorseName: name,
            JockeyName: $"騎手{number}", TrainerName: $"調教師{number}", HorseSourceIdentity: sourceIdentity);

    private static string SourceIdentity(string suffix) =>
        $"https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002036{suffix}/00";

    private static async Task<(WebApplication App, HttpClient Client)> CreateApplicationAsync()
    {
        var result = await TestApplicationFactory.CreateAsync();
        result.Client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = result.App.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", ResourceType.Horse,
            3, "Identity guard test", true);
        await store.RegisterDefinitionAsync(new("jockey-profile"), "Jockey profile", ResourceType.Jockey,
            3, "Identity guard test", true);
        await store.RegisterDefinitionAsync(new("trainer-profile"), "Trainer profile", ResourceType.Trainer,
            3, "Identity guard test", true);
        return result;
    }

    private static async Task<DeclareRaceResultBulkResponse> ReadBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>(JsonOptions);
        Assert.IsNotNull(body);
        return body;
    }

    private static async Task<string> GetContextJsonAsync(HttpClient client, string raceId)
    {
        using var response = await client.GetAsync($"/api/races/{raceId}/context");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static int CountStoredEvents(WebApplication app)
    {
        var provider = app.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>();
        using var db = provider.CreateContext();
        return db.Set<EventEntity>().Count();
    }

    private static async Task<int> CountTasksAsync(WebApplication app)
    {
        var store = app.Services.GetRequiredService<CollectionPlatformStore>();
        return (await store.GetTasksAsync()).Count;
    }
}
