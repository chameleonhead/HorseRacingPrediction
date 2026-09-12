using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionPlatformWorkerClientTests
{
    [TestMethod]
    public async Task Completion_SendsPerCandidateLocationOutcomes()
    {
        var taskId = Guid.NewGuid();
        var lease = new LeasedCollectionTask(taskId, Guid.NewGuid(), new(ResourceType.RaceCard, "JRA", "R1"),
            new("race-card"), 1, CollectionReason.Initial, CollectionLane.Realtime, 80,
            "lease", DateTimeOffset.UtcNow.AddMinutes(15), null, new Dictionary<string, string>());
        var transport = new RecordingTransport(lease);
        var client = new CollectionPlatformWorkerClient(new HttpClient(transport) { BaseAddress = new("https://api.test/") },
            new CollectionDefinitionHandlerRegistry([new OutcomeHandler()]));

        await client.ExecuteAsync(new(taskId, 1), CancellationToken.None);

        using var document = JsonDocument.Parse(transport.CompletionBody!);
        var outcomes = document.RootElement.GetProperty("locationOutcomes");
        Assert.AreEqual(2, outcomes.GetArrayLength());
        Assert.AreEqual(41, outcomes[0].GetProperty("locationId").GetInt64());
        Assert.AreEqual((int)CollectionAttemptResult.UnexpectedPage, outcomes[0].GetProperty("result").GetInt32());
        Assert.AreEqual((int)CollectionAttemptResult.Succeeded, outcomes[1].GetProperty("result").GetInt32());
    }

    [TestMethod]
    public async Task Cancellation_ReportsRetryableAttemptThroughCompletionEndpoint()
    {
        var taskId = Guid.NewGuid();
        var lease = new LeasedCollectionTask(taskId, Guid.NewGuid(), new(ResourceType.Horse, "JRA", "H1"),
            new("horse-profile"), 1, CollectionReason.Initial, CollectionLane.Normal, 50,
            "lease", DateTimeOffset.UtcNow.AddMinutes(15), null, new Dictionary<string, string>());
        var transport = new RecordingTransport(lease);
        using var cancellation = new CancellationTokenSource();
        var client = new CollectionPlatformWorkerClient(new HttpClient(transport) { BaseAddress = new("https://api.test/") },
            new CollectionDefinitionHandlerRegistry([new CancellingHandler(cancellation.Cancel)]));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.ExecuteAsync(new(taskId, 1), cancellation.Token));

        Assert.IsNotNull(transport.CompletionBody);
        using var document = JsonDocument.Parse(transport.CompletionBody);
        Assert.AreEqual((int)CollectionAttemptResult.TransientFailure,
            document.RootElement.GetProperty("result").GetInt32());
        Assert.AreEqual("CollectorTimeout", document.RootElement.GetProperty("errorCode").GetString());
    }

    [TestMethod]
    [DataRow(404, CollectionAttemptResult.ResourceNotFound)]
    [DataRow(429, CollectionAttemptResult.AccessLimited)]
    [DataRow(503, CollectionAttemptResult.TransientFailure)]
    public async Task HandlerHttpFailure_IsReportedWithRetrySafeClassification(
        int statusCode, CollectionAttemptResult expectedResult)
    {
        var taskId = Guid.NewGuid();
        var lease = new LeasedCollectionTask(taskId, Guid.NewGuid(), new(ResourceType.Horse, "JRA", "H1"),
            new("horse-profile"), 1, CollectionReason.Initial, CollectionLane.Normal, 50,
            "lease", DateTimeOffset.UtcNow.AddMinutes(15), null, new Dictionary<string, string>());
        var transport = new RecordingTransport(lease);
        var client = new CollectionPlatformWorkerClient(
            new HttpClient(transport) { BaseAddress = new("https://api.test/") },
            new CollectionDefinitionHandlerRegistry([new ThrowingHandler(
                new HttpRequestException("failure", null, (HttpStatusCode)statusCode))]));

        await client.ExecuteAsync(new(taskId, 1), CancellationToken.None);

        using var document = JsonDocument.Parse(transport.CompletionBody!);
        Assert.AreEqual((int)expectedResult, document.RootElement.GetProperty("result").GetInt32());
    }

    [TestMethod]
    public async Task HandlerTimeout_IsReportedAsTransientFailure()
    {
        var taskId = Guid.NewGuid();
        var lease = new LeasedCollectionTask(taskId, Guid.NewGuid(), new(ResourceType.Horse, "JRA", "H1"),
            new("horse-profile"), 1, CollectionReason.Initial, CollectionLane.Normal, 50,
            "lease", DateTimeOffset.UtcNow.AddMinutes(15), null, new Dictionary<string, string>());
        var transport = new RecordingTransport(lease);
        var client = new CollectionPlatformWorkerClient(
            new HttpClient(transport) { BaseAddress = new("https://api.test/") },
            new CollectionDefinitionHandlerRegistry([new ThrowingHandler(new TimeoutException("timeout"))]));

        await client.ExecuteAsync(new(taskId, 1), CancellationToken.None);

        using var document = JsonDocument.Parse(transport.CompletionBody!);
        Assert.AreEqual((int)CollectionAttemptResult.TransientFailure,
            document.RootElement.GetProperty("result").GetInt32());
    }

    private sealed class CancellingHandler(Action cancel) : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new("horse-profile");
        public ResourceType ResourceType => ResourceType.Horse;
        public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken token)
        {
            cancel();
            return Task.FromCanceled<CollectionAttemptCompletion>(token);
        }
    }

    private sealed class OutcomeHandler : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new("race-card");
        public ResourceType ResourceType => ResourceType.RaceCard;
        public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken token)
            => Task.FromResult(new CollectionAttemptCompletion(CollectionAttemptResult.Succeeded,
                LocationOutcomes:
                [
                    new(41, CollectionAttemptResult.UnexpectedPage, "UnexpectedPage"),
                    new(42, CollectionAttemptResult.Succeeded),
                ]));
    }

    private sealed class ThrowingHandler(Exception exception) : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new("horse-profile");
        public ResourceType ResourceType => ResourceType.Horse;
        public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken token)
            => Task.FromException<CollectionAttemptCompletion>(exception);
    }

    private sealed class RecordingTransport(LeasedCollectionTask lease) : HttpMessageHandler
    {
        public string? CompletionBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/acquire", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(lease) };
            CompletionBody = await request.Content!.ReadAsStringAsync(token);
            return new(HttpStatusCode.NoContent);
        }
    }
}
