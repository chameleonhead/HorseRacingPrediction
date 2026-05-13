using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using HorseRacingPrediction.AgentClient.Http;
using HorseRacingPrediction.Agents.Plugins;

namespace HorseRacingPrediction.AgentClient.Tests.Http;

[TestClass]
public sealed class HttpDataCollectionWriteServiceTests
{
    [TestMethod]
    public async Task UpsertRaceAsync_WhenCreateConflicts_FallsBackToPatchAndPublishesCard()
    {
        var raceId = DeterministicIdGenerator.BuildRaceId(new DateOnly(2025, 6, 15), "TOKYO", 5);
        var handler = new StubHttpMessageHandler();
        handler.Add(HttpMethod.Get, $"/api/races/{raceId}/context", new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.Add(HttpMethod.Post, "/api/races", new HttpResponseMessage(HttpStatusCode.Conflict));
        handler.Add(HttpMethod.Patch, $"/api/races/{raceId}", new HttpResponseMessage(HttpStatusCode.OK));
        handler.Add(HttpMethod.Post, $"/api/races/{raceId}/card/publish", new HttpResponseMessage(HttpStatusCode.OK));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var service = new HttpDataCollectionWriteService(httpClient);

        var savedRaceId = await service.UpsertRaceAsync(
            "2025-06-15",
            "TOKYO",
            5,
            "皐月賞",
            18,
            "G1",
            "TURF",
            2000,
            "RIGHT");

        Assert.AreEqual(raceId, savedRaceId);
        CollectionAssert.AreEqual(
            new[]
            {
                $"GET /api/races/{raceId}/context",
                "POST /api/races",
                $"PATCH /api/races/{raceId}",
                $"POST /api/races/{raceId}/card/publish"
            },
            handler.Requests);
    }

    [TestMethod]
    public async Task UpsertRaceAsync_WhenPublishConflicts_TreatsItAsSuccess()
    {
        var raceId = DeterministicIdGenerator.BuildRaceId(new DateOnly(2025, 6, 15), "TOKYO", 5);
        var handler = new StubHttpMessageHandler();
        handler.Add(HttpMethod.Get, $"/api/races/{raceId}/context", new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.Add(HttpMethod.Post, "/api/races", new HttpResponseMessage(HttpStatusCode.Conflict));
        handler.Add(HttpMethod.Patch, $"/api/races/{raceId}", new HttpResponseMessage(HttpStatusCode.OK));
        handler.Add(HttpMethod.Post, $"/api/races/{raceId}/card/publish", new HttpResponseMessage(HttpStatusCode.Conflict));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var service = new HttpDataCollectionWriteService(httpClient);

        var savedRaceId = await service.UpsertRaceAsync(
            "2025-06-15",
            "TOKYO",
            5,
            "皐月賞",
            18,
            "G1",
            "TURF",
            2000,
            "RIGHT");

        Assert.AreEqual(raceId, savedRaceId);
    }

    [TestMethod]
    public async Task UpsertRaceEntryAsync_WhenContextIsMissing_StillRegistersEntry()
    {
        const string raceId = "race-test";
        var handler = new StubHttpMessageHandler();
        handler.Add(HttpMethod.Get, $"/api/races/{raceId}/context", new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.Add(HttpMethod.Get, "/api/horses/horse-f4977642-14d8-5782-a6f2-a38c91f53f5a", new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.Add(HttpMethod.Post, "/api/horses", new HttpResponseMessage(HttpStatusCode.Created));
        handler.Add(HttpMethod.Get, "/api/jockeys/jockey-b6b2e32d-62de-5c88-84f9-30c4d1220c13", new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.Add(HttpMethod.Post, "/api/jockeys", new HttpResponseMessage(HttpStatusCode.Created));
        handler.Add(HttpMethod.Get, "/api/trainers/trainer-15595a65-f0d2-5181-8c3d-d7cb3af4d67c", new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.Add(HttpMethod.Post, "/api/trainers", new HttpResponseMessage(HttpStatusCode.Created));
        handler.Add(HttpMethod.Post, $"/api/races/{raceId}/entries", new HttpResponseMessage(HttpStatusCode.Created));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var service = new HttpDataCollectionWriteService(httpClient);

        var message = await service.UpsertRaceEntryAsync(
            raceId,
            1,
            "テストホース",
            "テスト騎手",
            "テスト調教師",
            1,
            55.0m,
            "M",
            3,
            470,
            2);

        StringAssert.Contains(message, raceId);
        CollectionAssert.Contains(handler.Requests, $"POST /api/races/{raceId}/entries");
    }

    [TestMethod]
    public async Task DeclareRaceResultAsync_WhenContextIsMissing_StillDeclaresResult()
    {
        const string raceId = "race-test";
        var handler = new StubHttpMessageHandler();
        handler.Add(HttpMethod.Get, $"/api/races/{raceId}/context", new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.Add(HttpMethod.Post, $"/api/races/{raceId}/result", new HttpResponseMessage(HttpStatusCode.OK));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var service = new HttpDataCollectionWriteService(httpClient);

        var message = await service.DeclareRaceResultAsync(raceId, "テストホース", null, null);

        StringAssert.Contains(message, raceId);
        CollectionAssert.AreEqual(
            new[]
            {
                $"GET /api/races/{raceId}/context",
                $"POST /api/races/{raceId}/result"
            },
            handler.Requests);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<HttpResponseMessage>> _responses = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public void Add(HttpMethod method, string pathAndQuery, HttpResponseMessage response)
        {
            var key = BuildKey(method, pathAndQuery);
            if (!_responses.TryGetValue(key, out var queue))
            {
                queue = new Queue<HttpResponseMessage>();
                _responses[key] = queue;
            }

            queue.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = BuildKey(request.Method, request.RequestUri!.PathAndQuery);
            Requests.Add(key);

            if (_responses.TryGetValue(key, out var queue) && queue.Count > 0)
            {
                return Task.FromResult(queue.Dequeue());
            }

            throw new AssertFailedException($"Unexpected request: {key}");
        }

        private static string BuildKey(HttpMethod method, string pathAndQuery)
        {
            return $"{method.Method.ToUpperInvariant()} {pathAndQuery}";
        }
    }
}