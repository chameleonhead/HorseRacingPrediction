using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.PredictionScheduling;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class HttpPredictionScheduleClientTests
{
    [TestMethod]
    public async Task HttpClient_UsesCanonicalResourceMethodsPathsAndTransitionPayloads()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://api.example.test/") };
        var schedule = new HttpPredictionSchedule(http);
        var now = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        var availableAt = now.AddMinutes(30);
        handler.Responses.Enqueue(new(HttpStatusCode.Accepted));
        handler.Responses.Enqueue(new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new PredictionCandidateLease("race-1", "lease-1"),
                new PredictionCandidateLease("race-2", "lease-2"),
            }),
        });
        handler.Responses.Enqueue(new(HttpStatusCode.NoContent));
        handler.Responses.Enqueue(new(HttpStatusCode.NoContent));

        await schedule.EnqueueAsync(["race-1", "race-2"], now);
        var leases = await schedule.AcquireAsync(now, TimeSpan.FromMinutes(5), 2, TimeSpan.FromMinutes(3));
        var completed = await schedule.CompleteAsync("race-1", "lease-1");
        var requeued = await schedule.RequeueAsync("race-2", "lease-2", availableAt, "temporary failure");

        Assert.HasCount(4, handler.Requests);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        Assert.AreEqual("/api/v2/internal/prediction-candidates", handler.Requests[0].Uri!.AbsolutePath);
        using (var enqueue = JsonDocument.Parse(handler.Requests[0].Body!))
        {
            CollectionAssert.AreEqual(new[] { "race-1", "race-2" },
                enqueue.RootElement.GetProperty("raceIds").EnumerateArray()
                    .Select(x => x.GetString()).ToArray());
        }

        Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
        Assert.AreEqual("/api/v2/internal/prediction-candidate-leases", handler.Requests[1].Uri!.AbsolutePath);
        Assert.HasCount(2, leases);
        Assert.AreEqual("lease-1", leases[0].LeaseToken);

        Assert.AreEqual(HttpMethod.Patch, handler.Requests[2].Method);
        Assert.AreEqual("/api/v2/internal/prediction-candidates/race-1", handler.Requests[2].Uri!.AbsolutePath);
        using (var complete = JsonDocument.Parse(handler.Requests[2].Body!))
        {
            Assert.AreEqual("Complete", complete.RootElement.GetProperty("mode").GetString());
            Assert.AreEqual("lease-1", complete.RootElement.GetProperty("leaseToken").GetString());
        }

        Assert.AreEqual(HttpMethod.Patch, handler.Requests[3].Method);
        Assert.AreEqual("/api/v2/internal/prediction-candidates/race-2", handler.Requests[3].Uri!.AbsolutePath);
        using (var requeue = JsonDocument.Parse(handler.Requests[3].Body!))
        {
            Assert.AreEqual("Requeue", requeue.RootElement.GetProperty("mode").GetString());
            Assert.AreEqual("lease-2", requeue.RootElement.GetProperty("leaseToken").GetString());
            Assert.AreEqual("temporary failure", requeue.RootElement.GetProperty("error").GetString());
            Assert.AreEqual(availableAt, requeue.RootElement.GetProperty("availableAt").GetDateTimeOffset());
        }
        Assert.IsTrue(completed);
        Assert.IsTrue(requeued);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Queue<HttpResponseMessage> Responses { get; } = new();
        public List<(HttpMethod Method, Uri? Uri, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return Responses.Dequeue();
        }
    }
}
