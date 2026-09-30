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
        Assert.HasCount(1, result.Marks);
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

    [TestMethod]
    public async Task GetMemosBySubjectAsync_WhenFound_PreservesSnapshotFieldsAndListOrder()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add(HttpMethod.Get, "/api/memos/by-subject/Race/race-001", new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new
                {
                    memoId = "memo-001",
                    authorId = (string?)null,
                    memoType = "Note",
                    content = "Race memo",
                    createdAt = DateTimeOffset.Parse("2026-07-09T09:00:00+09:00"),
                    subjects = new[]
                    {
                        new { subjectType = "Race", subjectId = "race-001" },
                        new { subjectType = "Horse", subjectId = "horse-001" }
                    },
                    links = new[]
                    {
                        new { linkId = "link-001", linkType = "web", title = "Article", url = (string?)null, storageKey = "storage-001" }
                    }
                }
            })
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var service = new HttpRaceQueryService(httpClient);

        var result = await service.GetMemosBySubjectAsync("Race", "race-001");

        Assert.IsNotNull(result);
        Assert.AreEqual("RACE:race-001", result!.SubjectKey);
        Assert.AreEqual(1, result.Memos.Count);
        Assert.AreEqual("memo-001", result.Memos[0].MemoId);
        Assert.IsNull(result.Memos[0].AuthorId);
        Assert.AreEqual("Race memo", result.Memos[0].Content);
        Assert.AreEqual(2, result.Memos[0].Subjects.Count);
        Assert.AreEqual("Race", result.Memos[0].Subjects[0].SubjectType);
        Assert.AreEqual("Horse", result.Memos[0].Subjects[1].SubjectType);
        Assert.AreEqual("Article", result.Memos[0].Links[0].Title);
        Assert.IsNull(result.Memos[0].Links[0].Url);
        Assert.AreEqual("storage-001", result.Memos[0].Links[0].StorageKey);
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
