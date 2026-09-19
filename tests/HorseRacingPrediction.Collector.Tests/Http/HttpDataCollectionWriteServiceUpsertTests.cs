using System.Net;
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
}
