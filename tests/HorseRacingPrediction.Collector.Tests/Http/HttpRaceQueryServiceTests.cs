using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using HorseRacingPrediction.Collector.Http;

namespace HorseRacingPrediction.Collector.Tests.Http;

[TestClass]
public sealed class HttpRaceQueryServiceTests
{
    [TestMethod]
    public async Task GetPredictionTicketAsync_WhenFound_ReturnsSummary()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add(HttpMethod.Get, "/api/predictions/prediction-001", new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                predictionTicketId = "prediction-001",
                raceId = "race-001",
                predictorType = "ApiOnlyPredictor",
                predictorId = "api-only-v1",
                confidenceScore = 80.5m,
                summaryComment = "テスト予想",
                predictedAt = DateTimeOffset.Parse("2026-07-09T00:00:00Z"),
                marks = new[]
                {
                    new { entryId = "entry-01", markCode = "◎", predictedRank = 1, score = 90m, comment = "本命" }
                }
            })
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var service = new HttpRaceQueryService(httpClient);

        var result = await service.GetPredictionTicketAsync("prediction-001");

        Assert.IsNotNull(result);
        Assert.AreEqual("prediction-001", result!.PredictionTicketId);
        Assert.AreEqual("race-001", result.RaceId);
        Assert.AreEqual(1, result.Marks.Count);
        Assert.AreEqual("◎", result.Marks[0].MarkCode);
    }

    [TestMethod]
    public async Task GetPredictionTicketAsync_WhenNotFound_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add(HttpMethod.Get, "/api/predictions/missing", new HttpResponseMessage(HttpStatusCode.NotFound));

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var service = new HttpRaceQueryService(httpClient);

        var result = await service.GetPredictionTicketAsync("missing");

        Assert.IsNull(result);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<HttpResponseMessage>> _responses = new(StringComparer.Ordinal);

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
