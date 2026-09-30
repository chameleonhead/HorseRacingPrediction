using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Collector.Http;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Collector.Tests.Http;

[TestClass]
public sealed class HttpDataCollectionWriteServiceUpsertTests
{
    [TestMethod]
    public async Task BulkWrite_NullSuccessResponseUsesExistingFallback_AndNonSuccessStillThrows()
    {
        var successHandler = new FixedResponseHandler(HttpStatusCode.OK, "null");
        using var successClient = new HttpClient(successHandler) { BaseAddress = new Uri("https://example.invalid") };
        var successService = new HttpDataCollectionWriteService(successClient, new AgentAcquisitionStatusRecorder());

        var result = await successService.DeclareRaceResultBulkAsync(null!);

        Assert.AreEqual(string.Empty, result.RaceId);
        Assert.HasCount(0, result.Errors);

        var failureHandler = new FixedResponseHandler(HttpStatusCode.Conflict, "{\"code\":\"identity-conflict\"}");
        using var failureClient = new HttpClient(failureHandler) { BaseAddress = new Uri("https://example.invalid") };
        var failureService = new HttpDataCollectionWriteService(failureClient, new AgentAcquisitionStatusRecorder());
        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => failureService.DeclareRaceResultBulkAsync(null!));
    }

    [TestMethod]
    public async Task SubjectUpserts_SendOnePutAndNoExistenceGet()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        var sut = new HttpDataCollectionWriteService(client, new AgentAcquisitionStatusRecorder());

        await sut.UpsertHorseAsync("テスト馬", null, "F", null);
        await sut.UpsertJockeyAsync("テスト騎手", null, "JRA");
        await sut.UpsertTrainerAsync("テスト調教師", null, "JRA");

        Assert.HasCount(4, handler.Requests);
        Assert.AreEqual((HttpMethod.Post, "/api/identity/horse"), handler.Requests[0]);
        Assert.IsTrue(handler.Requests.Skip(1).All(request => request.Method == HttpMethod.Put));
        CollectionAssert.AreEquivalent(
            new[] { "/api/horses/", "/api/jockeys/", "/api/trainers/" },
            handler.Requests.Skip(1).Select(request => request.Path[..(request.Path.LastIndexOf('/') + 1)]).ToArray());
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
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri.AbsolutePath == "/api/identity/horse"
                    ? JsonContent.Create(new ResolvedIdentityDto("horse-legacy")) : null
            });
        }
    }

    private sealed class FixedResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            });
    }

    [TestMethod]
    public async Task EntryRecollection_ResolvesCurrentRiderWithoutRenamingOldMasterOrMovingHorse()
    {
        var handler = new EntryHandler("horse-existing", jockeyId: "jockey-contaminated");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        var sut = new HttpDataCollectionWriteService(client, new AgentAcquisitionStatusRecorder());
        await sut.UpsertRaceEntryAsync("race-test", 8, "テスト馬", "幸 英明", null, null, null, null, null, null, null);
        var correctId = DeterministicIdGenerator.BuildEntityId("jockey",
            DeterministicIdGenerator.NormalizeKey("幸 英明"));
        Assert.IsTrue(handler.Writes.Any(x => x.Path == $"/api/jockeys/{correctId}"));
        Assert.IsFalse(handler.Writes.Any(x => x.Path.Contains("jockey-contaminated", StringComparison.Ordinal)));
        using var json = JsonDocument.Parse(handler.Writes.Single(x => x.Path.EndsWith("/entries")).Body);
        Assert.AreEqual(correctId, json.RootElement.GetProperty("jockeyId").GetString());
        Assert.AreEqual("horse-existing", json.RootElement.GetProperty("horseId").GetString());
        Assert.AreEqual(DeterministicIdGenerator.BuildRaceEntryId("race-test", "horse-existing"),
            json.RootElement.GetProperty("entryId").GetString());
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

    private sealed class EntryHandler(string horseId, HttpStatusCode resultStatus = HttpStatusCode.OK,
        string? jockeyId = null) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Writes { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/identity/horse")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ResolvedIdentityDto(horseId)) };
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
                        horseNumber = 8, gateNumber = 4, jockeyId, runningStyleCode = "逃", ownerName = "所有者" } }
                    })
                    : JsonContent.Create(new { horseId, registeredName = "テスト馬" }),
            };
        }
    }
}
