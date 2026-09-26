using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Collector.Http;

namespace HorseRacingPrediction.Collector.Tests.Http;

[TestClass]
public sealed class HttpDataCollectionWriteServiceUpsertTests
{
    [TestMethod]
    public async Task SubjectUpserts_SendOnePutAndNoExistenceGet()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        var sut = new HttpDataCollectionWriteService(client, new AgentAcquisitionStatusRecorder());

        await sut.UpsertHorseAsync("テスト馬", null, "F", null);
        await sut.UpsertJockeyAsync("テスト騎手", null, "JRA");
        await sut.UpsertTrainerAsync("テスト調教師", null, "JRA");

        Assert.HasCount(3, handler.Requests);
        Assert.IsTrue(handler.Requests.All(request => request.Method == HttpMethod.Put));
        CollectionAssert.AreEquivalent(
            new[] { "/api/horses/", "/api/jockeys/", "/api/trainers/" },
            handler.Requests.Select(request => request.Path[..(request.Path.LastIndexOf('/') + 1)]).ToArray());
    }

    [TestMethod]
    public async Task JockeyAndTrainerUpserts_UseTheSameCanonicalIdsAsRaceBulk()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        var sut = new HttpDataCollectionWriteService(client, new AgentAcquisitionStatusRecorder());

        await sut.UpsertJockeyAsync("▲識別 騎手", null, "JRA");
        await sut.UpsertTrainerAsync("識別 調教師（美浦）", null, "JRA");

        var jockeyId = DeterministicIdGenerator.BuildEntityId("jockey",
            DeterministicIdGenerator.NormalizeKey("識別 騎手"));
        var trainerId = DeterministicIdGenerator.BuildEntityId("trainer",
            DeterministicIdGenerator.NormalizeKey("識別 調教師"));
        CollectionAssert.AreEquivalent(
            new[] { $"/api/jockeys/{jockeyId}", $"/api/trainers/{trainerId}" },
            handler.Requests.Select(request => request.Path).ToArray());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [TestMethod]
    public async Task EntryUpdate_UsesHorseIdentityAndRetainsCollectedAttributes()
    {
        var horseId = DeterministicIdGenerator.BuildHorseId("テスト馬");
        var handler = new EntryHandler(horseId);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        var sut = new HttpDataCollectionWriteService(client, new AgentAcquisitionStatusRecorder());
        await sut.UpsertRaceEntryAsync("race-test", 9, "テスト馬", null, null, null, null, null, null, null, null);
        var registration = handler.Writes.Single(write => write.Path.EndsWith("/entries"));
        using var json = JsonDocument.Parse(registration.Body);
        Assert.AreEqual(DeterministicIdGenerator.BuildRaceEntryId("race-test", horseId), json.RootElement.GetProperty("entryId").GetString());
        Assert.AreEqual(9, json.RootElement.GetProperty("horseNumber").GetInt32());
        Assert.AreEqual("逃", json.RootElement.GetProperty("runningStyleCode").GetString());
        Assert.AreEqual("所有者", json.RootElement.GetProperty("ownerName").GetString());
    }

    [TestMethod]
    [DataRow(HttpStatusCode.OK)]
    [DataRow(HttpStatusCode.Conflict)]
    public async Task EntryResult_UsesHorseIdentityAndDoesNotSwallowConflict(HttpStatusCode status)
    {
        const string horseId = "horse-test";
        var handler = new EntryHandler(horseId, status);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        var sut = new HttpDataCollectionWriteService(client, new AgentAcquisitionStatusRecorder());
        var operation = () => sut.DeclareRaceEntryResultAsync("race-test", horseId, 1, null, null, null, null, null);
        if (status == HttpStatusCode.Conflict)
            await Assert.ThrowsExactlyAsync<HttpRequestException>(operation);
        else
            await operation();
        Assert.AreEqual($"/api/races/race-test/entries/{DeterministicIdGenerator.BuildRaceEntryId("race-test", horseId)}/result",
            handler.Writes.Single().Path);
    }

    private sealed class EntryHandler(string horseId, HttpStatusCode resultStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Writes { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method != HttpMethod.Get)
            {
                Writes.Add((path, await request.Content!.ReadAsStringAsync(cancellationToken)));
                return new HttpResponseMessage(path.EndsWith("/result") ? resultStatus : HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = path.EndsWith("/context")
                    ? JsonContent.Create(new
                    {
                        raceId = "race-test",
                        entries = new[] { new {
                        entryId = DeterministicIdGenerator.BuildRaceEntryId("race-test", horseId), horseId,
                        horseNumber = 8, gateNumber = 4, runningStyleCode = "逃", ownerName = "所有者" } }
                    })
                    : JsonContent.Create(new { horseId, registeredName = "テスト馬" }),
            };
        }
    }
}
