using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;

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
    public async Task Acquire_SendsCurrentExecutionCorrelationToApi()
    {
        var taskId = Guid.NewGuid();
        var lease = new LeasedCollectionTask(taskId, Guid.NewGuid(), new(ResourceType.RaceCard, "JRA", "R1"),
            new("race-card"), 1, CollectionReason.Initial, CollectionLane.Realtime, 80,
            "lease", DateTimeOffset.UtcNow.AddMinutes(15), null, new Dictionary<string, string>());
        var transport = new RecordingTransport(lease);
        var client = new CollectionPlatformWorkerClient(
            new HttpClient(transport) { BaseAddress = new("https://api.test/") },
            new CollectionDefinitionHandlerRegistry([new OutcomeHandler()]));
        var expected = new CollectionAttemptCorrelation(Guid.NewGuid(), Guid.NewGuid(),
            "sqs-message", "lambda-request", 2, 12);

        using (CollectionAttemptCorrelationScope.Push(expected))
            await client.ExecuteAsync(new(taskId, 1), CancellationToken.None);

        using var document = JsonDocument.Parse(transport.AcquireBody!);
        var correlation = document.RootElement.GetProperty("correlation");
        Assert.AreEqual(expected.ExecutionBatchId, correlation.GetProperty("executionBatchId").GetGuid());
        Assert.AreEqual(expected.DispatchEnvelopeId, correlation.GetProperty("dispatchEnvelopeId").GetGuid());
        Assert.AreEqual("sqs-message", correlation.GetProperty("queueMessageId").GetString());
        Assert.AreEqual("lambda-request", correlation.GetProperty("lambdaRequestId").GetString());
        Assert.AreEqual(2, correlation.GetProperty("batchTaskOrdinal").GetInt32());
        Assert.AreEqual(12, correlation.GetProperty("batchTaskCount").GetInt32());
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

    [TestMethod]
    [DataRow(CollectionTaskAcquireStatus.AlreadyTerminal)]
    [DataRow(CollectionTaskAcquireStatus.SupersededGeneration)]
    public async Task ResolvedAcquireStatus_IsAcknowledgedWithoutRunningHandlerOrCompleting(
        CollectionTaskAcquireStatus status)
    {
        var transport = new AcquireStatusTransport(new(status));
        var handler = new CountingHandler();
        var client = new CollectionPlatformWorkerClient(
            new HttpClient(transport) { BaseAddress = new("https://api.test/") },
            new CollectionDefinitionHandlerRegistry([handler]));

        await client.ExecuteAsync(new(Guid.NewGuid(), 1), CancellationToken.None);

        Assert.AreEqual(0, handler.CallCount);
        Assert.IsNull(transport.CompletionBody);
    }

    [TestMethod]
    public async Task ActiveElsewhere_IsReportedToEnvelopeExecutorForRedelivery()
    {
        var taskId = Guid.NewGuid();
        var transport = new AcquireStatusTransport(new(CollectionTaskAcquireStatus.ActiveElsewhere));
        var client = new CollectionPlatformWorkerClient(
            new HttpClient(transport) { BaseAddress = new("https://api.test/") },
            new CollectionDefinitionHandlerRegistry([new CountingHandler()]));

        await Assert.ThrowsAsync<CollectionTaskActiveElsewhereException>(
            () => client.ExecuteAsync(new(taskId, 1), CancellationToken.None));
        Assert.IsNull(transport.CompletionBody);
    }

    [TestMethod]
    public async Task EnvelopeCompatibilityMismatch_IsLeftUnresolvedWithoutRunningHandler()
    {
        var taskId = Guid.NewGuid();
        var lease = new LeasedCollectionTask(taskId, Guid.NewGuid(), new(ResourceType.RaceCard, "JRA", "R1"),
            new("race-card"), 1, CollectionReason.Initial, CollectionLane.Background, 80,
            "lease", DateTimeOffset.UtcNow.AddMinutes(15), new DateOnly(2026, 9, 12),
            new Dictionary<string, string>());
        var transport = new AcquireStatusTransport(new(CollectionTaskAcquireStatus.Acquired, lease));
        var handler = new CountingHandler();
        var client = new CollectionPlatformWorkerClient(
            new HttpClient(transport) { BaseAddress = new("https://api.test/") },
            new CollectionDefinitionHandlerRegistry([handler]));
        var factory = new FakeJraSessionFactory();

        await Assert.ThrowsAsync<CollectionDispatchCompatibilityException>(() =>
            JraSessionExecutionScope.ExecuteAsync(factory,
                token => client.ExecuteAsync(new(taskId, 1), token), CancellationToken.None,
                new("JRA", new("race-card"), new DateOnly(2026, 9, 12), CollectionLane.Realtime)));

        Assert.AreEqual(0, handler.CallCount);
        Assert.IsNull(transport.CompletionBody);
        Assert.AreEqual(0, factory.CreateCallCount);
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

    private sealed class CountingHandler : ICollectionDefinitionHandler
    {
        public int CallCount { get; private set; }
        public CollectionDefinitionId DefinitionId => new("race-card");
        public ResourceType ResourceType => ResourceType.RaceCard;
        public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken token)
        {
            CallCount++;
            return Task.FromResult(new CollectionAttemptCompletion(CollectionAttemptResult.Succeeded));
        }
    }

    private sealed class AcquireStatusTransport(CollectionTaskAcquireResult acquire) : HttpMessageHandler
    {
        public string? CompletionBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/acquire", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(acquire) };
            CompletionBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.NoContent);
        }
    }

    private sealed class RecordingTransport(LeasedCollectionTask lease) : HttpMessageHandler
    {
        public string? AcquireBody { get; private set; }
        public string? CompletionBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/acquire", StringComparison.Ordinal))
            {
                AcquireBody = await request.Content!.ReadAsStringAsync(token);
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new CollectionTaskAcquireResult(
                        CollectionTaskAcquireStatus.Acquired, lease)),
                };
            }
            CompletionBody = await request.Content!.ReadAsStringAsync(token);
            return new(HttpStatusCode.NoContent);
        }
    }
}
