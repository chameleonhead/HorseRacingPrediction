using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using HorseRacingPrediction.AgentClient.Http;
using HorseRacingPrediction.AgentClient.Scheduling;
using HorseRacingPrediction.Agents.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Tests.Http;

[TestClass]
public sealed class HttpDataCollectionWriteServiceTests
{
    private string _stateDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _stateDirectory = Path.Combine(Path.GetTempPath(), "http-data-collection-write-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stateDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }

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
        var service = new HttpDataCollectionWriteService(httpClient, CreateStatusRecorder());

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
        var service = new HttpDataCollectionWriteService(httpClient, CreateStatusRecorder());

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
    public async Task UpsertRaceAsync_WhenExistingRaceIsDraft_PublishesCard()
    {
        var raceId = DeterministicIdGenerator.BuildRaceId(new DateOnly(2025, 6, 15), "TOKYO", 5);
        var handler = new StubHttpMessageHandler();
        handler.Add(
            HttpMethod.Get,
            $"/api/races/{raceId}/context",
            StubHttpMessageHandler.JsonResponse(new HorseRacingPrediction.Agents.Contracts.RacePredictionContextReadModel
            {
                RaceId = raceId,
                Status = HorseRacingPrediction.Agents.Contracts.RaceStatus.Draft,
                Entries = []
            }));
        handler.Add(HttpMethod.Patch, $"/api/races/{raceId}", new HttpResponseMessage(HttpStatusCode.OK));
        handler.Add(HttpMethod.Post, $"/api/races/{raceId}/card/publish", new HttpResponseMessage(HttpStatusCode.OK));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var service = new HttpDataCollectionWriteService(httpClient, CreateStatusRecorder());

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
                $"PATCH /api/races/{raceId}",
                $"POST /api/races/{raceId}/card/publish"
            },
            handler.Requests);
    }

    [TestMethod]
    public async Task UpsertRaceEntryAsync_WhenContextIsMissing_StillRegistersEntry()
    {
        const string raceId = "race-test";
        var horseId = DeterministicIdGenerator.BuildEntityId("horse", DeterministicIdGenerator.NormalizeDisplayName("テストホース"));
        var jockeyId = DeterministicIdGenerator.BuildEntityId("jockey", DeterministicIdGenerator.NormalizeDisplayName("テスト騎手"));
        var trainerId = DeterministicIdGenerator.BuildEntityId("trainer", DeterministicIdGenerator.NormalizeDisplayName("テスト調教師"));
        var handler = new StubHttpMessageHandler();
        handler.Add(HttpMethod.Get, $"/api/races/{raceId}/context", new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.Add(HttpMethod.Get, $"/api/horses/{horseId}", new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.Add(HttpMethod.Post, "/api/horses", new HttpResponseMessage(HttpStatusCode.Created));
        handler.Add(HttpMethod.Get, $"/api/jockeys/{jockeyId}", new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.Add(HttpMethod.Post, "/api/jockeys", new HttpResponseMessage(HttpStatusCode.Created));
        handler.Add(HttpMethod.Get, $"/api/trainers/{trainerId}", new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.Add(HttpMethod.Post, "/api/trainers", new HttpResponseMessage(HttpStatusCode.Created));
        handler.Add(HttpMethod.Post, $"/api/races/{raceId}/entries", new HttpResponseMessage(HttpStatusCode.Created));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var service = new HttpDataCollectionWriteService(httpClient, CreateStatusRecorder());

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
        var service = new HttpDataCollectionWriteService(httpClient, CreateStatusRecorder());

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

    private AgentAcquisitionStatusRecorder CreateStatusRecorder()
    {
        var options = Options.Create(new AgentProcessingOptions
        {
            StateDirectory = _stateDirectory,
            PredictionLeaseMinutes = 5,
            CollectionLeaseMinutes = 5,
        });

        return new AgentAcquisitionStatusRecorder(
            new ProcessingStateStore(options, NullLogger<ProcessingStateStore>.Instance));
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

        public static HttpResponseMessage JsonResponse<T>(T value)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(value)
            };
        }
    }
}