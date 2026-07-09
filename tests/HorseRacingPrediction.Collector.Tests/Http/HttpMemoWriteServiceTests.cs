using System.Net;
using System.Net.Http;
using HorseRacingPrediction.Collector.Http;

namespace HorseRacingPrediction.Collector.Tests.Http;

[TestClass]
public sealed class HttpMemoWriteServiceTests
{
    [TestMethod]
    public async Task CreateOrUpdateRaceMemoAsync_WhenMemoDoesNotExist_Creates()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add(HttpMethod.Post, "/api/memos", new HttpResponseMessage(HttpStatusCode.Created));

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var service = new HttpMemoWriteService(httpClient);

        var memoId = await service.CreateOrUpdateRaceMemoAsync(
            "race-001", "SnsStoryPost", "本文", "PostGenerationWorkflow", "memo-post-prediction-001");

        Assert.AreEqual("memo-post-prediction-001", memoId);
        CollectionAssert.AreEqual(new[] { "POST /api/memos" }, handler.Requests);
    }

    [TestMethod]
    public async Task CreateOrUpdateRaceMemoAsync_WhenMemoAlreadyExists_FallsBackToUpdate()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add(HttpMethod.Post, "/api/memos", new HttpResponseMessage(HttpStatusCode.Conflict));
        handler.Add(HttpMethod.Put, "/api/memos/memo-post-prediction-001", new HttpResponseMessage(HttpStatusCode.OK));

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var service = new HttpMemoWriteService(httpClient);

        var memoId = await service.CreateOrUpdateRaceMemoAsync(
            "race-001", "SnsStoryPost", "再生成された本文", "PostGenerationWorkflow", "memo-post-prediction-001");

        Assert.AreEqual("memo-post-prediction-001", memoId);
        CollectionAssert.AreEqual(
            new[] { "POST /api/memos", "PUT /api/memos/memo-post-prediction-001" },
            handler.Requests);
    }

    [TestMethod]
    public async Task CreateOrUpdateRaceMemoAsync_WhenCreateFailsWithOtherError_Throws()
    {
        var handler = new StubHttpMessageHandler();
        handler.Add(HttpMethod.Post, "/api/memos", new HttpResponseMessage(HttpStatusCode.InternalServerError));

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var service = new HttpMemoWriteService(httpClient);

        try
        {
            await service.CreateOrUpdateRaceMemoAsync(
                "race-001", "SnsStoryPost", "本文", "PostGenerationWorkflow", "memo-post-prediction-001");
            Assert.Fail("InvalidOperationException が発生すべきです");
        }
        catch (InvalidOperationException)
        {
            // expected
        }
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
