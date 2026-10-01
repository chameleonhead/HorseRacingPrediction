using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionLambdaInvocationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions AcquireResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    [TestMethod]
    public async Task Envelope_ProcessesEveryTaskAndReturnsEmptyPartialFailureResponse()
    {
        var envelope = Envelope(3);
        var executed = new List<Guid>();

        var response = await CollectionLambdaInvocation.ExecuteAsync(Event("message-1", envelope),
            (task, _) => { executed.Add(task.TaskId); return Task.CompletedTask; });

        CollectionAssert.AreEqual(envelope.Tasks.Select(x => x.TaskId).ToArray(), executed);
        Assert.IsEmpty(response.BatchItemFailures);
        using var json = JsonDocument.Parse(CollectionLambdaInvocation.SerializeResponse(response));
        Assert.AreEqual(JsonValueKind.Array, json.RootElement.GetProperty("batchItemFailures").ValueKind);
    }

    [TestMethod]
    public async Task Envelope_ExposesStableExecutionCorrelationForEveryTask()
    {
        var envelope = Envelope(3);
        var correlations = new List<CollectionAttemptCorrelation>();

        var response = await CollectionLambdaInvocation.ExecuteAsync(Event("sqs-message-42", envelope),
            (_, _) =>
            {
                correlations.Add(CollectionAttemptCorrelationScope.Current!);
                return Task.CompletedTask;
            }, lambdaRequestId: "lambda-request-7");

        Assert.IsEmpty(response.BatchItemFailures);
        Assert.HasCount(3, correlations);
        Assert.AreEqual(1, correlations.Select(x => x.ExecutionBatchId).Distinct().Count());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, correlations.Select(x => x.BatchTaskOrdinal).ToArray());
        Assert.IsTrue(correlations.All(x => x.DispatchEnvelopeId == envelope.EnvelopeId));
        Assert.IsTrue(correlations.All(x => x.QueueMessageId == "sqs-message-42"));
        Assert.IsTrue(correlations.All(x => x.LambdaRequestId == "lambda-request-7"));
        Assert.IsTrue(correlations.All(x => x.BatchTaskCount == 3));
    }

    [TestMethod]
    public async Task TwelveRaceCards_InOneEnvelope_CreateOneJraSession()
    {
        var envelope = Envelope(12);
        var factory = new FakeJraSessionFactory();
        var executed = 0;

        var response = await CollectionLambdaInvocation.ExecuteAsync(Event("race-card-day", envelope),
            async (_, cancellationToken) =>
            {
                await using var lease = await JraSessionExecutionScope.AcquireAsync(factory, cancellationToken);
                executed++;
            }, cancellationToken: CancellationToken.None,
            executeGroup: (_, operation, cancellationToken) =>
                JraSessionExecutionScope.ExecuteAsync(factory, operation, cancellationToken));

        Assert.AreEqual(12, executed);
        Assert.AreEqual(1, factory.CreateCallCount);
        Assert.IsEmpty(response.BatchItemFailures);
    }

    [TestMethod]
    public async Task SessionInitializationFailure_ReturnsEnvelopeForRedelivery()
    {
        var envelope = Envelope(3);
        var executed = 0;

        var response = await CollectionLambdaInvocation.ExecuteAsync(Event("browser-init", envelope),
            async (_, cancellationToken) =>
            {
                executed++;
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }, cancellationToken: CancellationToken.None,
            executeGroup: (_, _, _) => throw new InvalidOperationException("Browser initialization failed."));

        Assert.AreEqual(0, executed);
        Assert.AreEqual("browser-init", response.BatchItemFailures.Single().ItemIdentifier);
    }

    [TestMethod]
    public async Task TaskFailure_ReturnsEnvelopeMessageIdAndDoesNotStartRemainingTask()
    {
        var envelope = Envelope(3);
        var executed = new List<Guid>();

        var response = await CollectionLambdaInvocation.ExecuteAsync(Event("message-2", envelope),
            (task, _) =>
            {
                executed.Add(task.TaskId);
                return executed.Count == 2 ? Task.FromException(new HttpRequestException("API unavailable"))
                    : Task.CompletedTask;
            });

        Assert.HasCount(2, executed);
        Assert.AreEqual("message-2", response.BatchItemFailures.Single().ItemIdentifier);
    }

    [TestMethod]
    public async Task ActiveElsewhere_ReturnsEnvelopeForRedeliveryButContinuesOtherTasks()
    {
        var envelope = Envelope(3);
        var executed = 0;

        var response = await CollectionLambdaInvocation.ExecuteAsync(Event("active-elsewhere", envelope),
            (task, _) =>
            {
                executed++;
                return executed == 2
                    ? Task.FromException(new CollectionTaskActiveElsewhereException(task.TaskId))
                    : Task.CompletedTask;
            });

        Assert.AreEqual(3, executed);
        Assert.AreEqual("active-elsewhere", response.BatchItemFailures.Single().ItemIdentifier);
    }

    [TestMethod]
    public async Task UnsupportedEnvelope_IsNotExecutedAndIsReturnedForRedelivery()
    {
        var envelope = Envelope(1) with { ContractVersion = 99 };
        var executed = 0;

        var response = await CollectionLambdaInvocation.ExecuteAsync(Event("unsupported", envelope),
            (_, _) => { executed++; return Task.CompletedTask; });

        Assert.AreEqual(0, executed);
        Assert.AreEqual("unsupported", response.BatchItemFailures.Single().ItemIdentifier);
    }

    [TestMethod]
    public async Task VersionOneEnvelope_RemainsExecutableDuringRollingUpgrade()
    {
        var envelope = Envelope(1) with { ContractVersion = 1 };
        var executed = 0;

        var response = await CollectionLambdaInvocation.ExecuteAsync(Event("version-one", envelope),
            (_, _) => { executed++; return Task.CompletedTask; });

        Assert.AreEqual(1, executed);
        Assert.IsEmpty(response.BatchItemFailures);
    }

    [TestMethod]
    public async Task TimeMarginReached_LeavesEnvelopeForRedeliveryWithoutStartingMoreTasks()
    {
        var envelope = Envelope(3);
        var executed = 0;

        var response = await CollectionLambdaInvocation.ExecuteAsync(Event("timeout", envelope),
            (_, _) => { executed++; return Task.CompletedTask; }, () => executed < 1);

        Assert.AreEqual(1, executed);
        Assert.AreEqual("timeout", response.BatchItemFailures.Single().ItemIdentifier);
    }

    [TestMethod]
    public async Task OneMalformedRecord_DoesNotPreventAnotherRecordFromCompleting()
    {
        var valid = Envelope(1);
        var eventJson = JsonSerializer.Serialize(new
        {
            Records = new object[]
            {
                new { messageId = "bad", body = "not-json" },
                new { messageId = "good", body = JsonSerializer.Serialize(valid, JsonOptions) },
            },
        });
        var executed = 0;

        var response = await CollectionLambdaInvocation.ExecuteAsync(eventJson,
            (_, _) => { executed++; return Task.CompletedTask; });

        Assert.AreEqual(1, executed);
        Assert.AreEqual("bad", response.BatchItemFailures.Single().ItemIdentifier);
    }

    [TestMethod]
    public async Task Wake_TypedSafeNoWorkIsAcknowledged()
    {
        var wake = new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "reservation");
        var worker = Worker(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AcquireNextExecutionResponse(ToWire(new CollectionExecutionAcquireResult(
                CollectionExecutionAcquireStatus.NoWork,
                NoWorkReason: CollectionExecutionNoWorkReason.PipelinePaused,
                ReservationReleaseOutcome: CollectionReservationReleaseOutcome.Released))),
                options: AcquireResponseJsonOptions)
        });

        var response = await CollectionLambdaInvocation.ExecuteWakeAsync(WakeEvent("wake", wake), worker);

        Assert.IsEmpty(response.BatchItemFailures);
    }

    [TestMethod]
    public async Task Wake_AcquireBadGatewayIsRetriedBySqs()
    {
        var wake = new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "reservation");
        var worker = Worker(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var response = await CollectionLambdaInvocation.ExecuteWakeAsync(WakeEvent("wake-502", wake), worker);

        Assert.AreEqual("wake-502", response.BatchItemFailures.Single().ItemIdentifier);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Wake_AcquireTransportOrTimeoutFailureIsRetriedBySqs(bool timeout)
    {
        var wake = new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "reservation");
        Exception exception = timeout
            ? new TaskCanceledException("API acquire timed out.")
            : new HttpRequestException("API transport failed.");
        var worker = Worker(_ => throw exception);

        var response = await CollectionLambdaInvocation.ExecuteWakeAsync(WakeEvent("wake-transport", wake), worker);

        Assert.AreEqual("wake-transport", response.BatchItemFailures.Single().ItemIdentifier);
    }

    [TestMethod]
    [DataRow(502)]
    [DataRow(503)]
    public async Task Wake_AcquireServerFailureIsRetriedBySqs(int statusCode)
    {
        var wake = new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "reservation");
        var worker = Worker(_ => new HttpResponseMessage((HttpStatusCode)statusCode));

        var response = await CollectionLambdaInvocation.ExecuteWakeAsync(WakeEvent("wake-server-error", wake), worker);

        Assert.AreEqual("wake-server-error", response.BatchItemFailures.Single().ItemIdentifier);
    }

    [TestMethod]
    [DataRow("not-json", 200)]
    [DataRow("{\"acquisition\":{\"status\":\"unknownAcquireStatus\"}}", 200)]
    [DataRow("{\"acquisition\":{\"status\":\"noWork\",\"noWorkReason\":\"futureReason\"}}", 200)]
    [DataRow("{\"acquisition\":{\"status\":\"noWork\",\"noWorkReason\":\"reservationUnavailable\"}}", 201)]
    public async Task Wake_MalformedOrAmbiguousAcquireResponseIsRetriedBySqs(string body, int statusCode)
    {
        var wake = new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "reservation");
        var worker = Worker(_ => new HttpResponseMessage((HttpStatusCode)statusCode)
        { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });

        var response = await CollectionLambdaInvocation.ExecuteWakeAsync(WakeEvent("wake-malformed-response", wake), worker);

        Assert.AreEqual("wake-malformed-response", response.BatchItemFailures.Single().ItemIdentifier);
    }

    [TestMethod]
    public async Task Wake_AcquiredResponseWithUnsupportedEnvelopeIsRetriedBySqs()
    {
        var envelope = Envelope(1) with
        {
            Compatibility = new CollectionDispatchCompatibilityKey("", new("definition"), null,
                CollectionLane.Normal),
        };
        var wake = new CollectionWakeSignal(Guid.NewGuid(), envelope.EnvelopeId, "reservation");
        var worker = Worker(_ => JsonContentResponse(HttpStatusCode.Created,
            new AcquireNextExecutionResponse(ToWire(new CollectionExecutionAcquireResult(
                CollectionExecutionAcquireStatus.Acquired, Guid.NewGuid(), "lease-token", envelope)))));

        var response = await CollectionLambdaInvocation.ExecuteWakeAsync(WakeEvent("wake-invalid-envelope", wake), worker);

        Assert.AreEqual("wake-invalid-envelope", response.BatchItemFailures.Single().ItemIdentifier);
    }

    [TestMethod]
    public async Task Wake_StaleAndDuplicateNoWorkAreAcknowledged()
    {
        var stale = new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "expired-token");
        var duplicate = new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "old-token");
        var worker = Worker(request =>
        {
            var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var wakeId = body.RootElement.GetProperty("acquisition").GetProperty("wake")
                .GetProperty("wakeId").GetGuid();
            var result = new CollectionExecutionAcquireResult(CollectionExecutionAcquireStatus.NoWork,
                NoWorkReason: wakeId == stale.WakeId
                    ? CollectionExecutionNoWorkReason.ReservationUnavailable
                    : CollectionExecutionNoWorkReason.LeaseConflict);
            return JsonContentResponse(HttpStatusCode.OK,
                new AcquireNextExecutionResponse(ToWire(result)));
        });
        var eventJson = JsonSerializer.Serialize(new
        {
            Records = new[]
            {
                new { messageId = "stale-wake", body = JsonSerializer.Serialize(stale, JsonOptions) },
                new { messageId = "duplicate-wake", body = JsonSerializer.Serialize(duplicate, JsonOptions) },
            },
        });

        var response = await CollectionLambdaInvocation.ExecuteWakeAsync(eventJson, worker);

        Assert.IsEmpty(response.BatchItemFailures);
    }

    [TestMethod]
    public async Task Wake_ActiveElsewhereContinuesLaterTasksAndCompletesExecution()
    {
        var envelope = Envelope(3);
        var wake = new CollectionWakeSignal(Guid.NewGuid(), envelope.EnvelopeId, "reservation");
        var executionBatchId = Guid.NewGuid();
        var taskAcquireCalls = new List<Guid>();
        var completeCalls = 0;
        var worker = Worker(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/execution-leases", StringComparison.Ordinal))
                return JsonContentResponse(HttpStatusCode.Created, new AcquireNextExecutionResponse(ToWire(
                    new CollectionExecutionAcquireResult(CollectionExecutionAcquireStatus.Acquired,
                        executionBatchId, "lease-token", envelope))));
            if (path.EndsWith("/execution-batches/" + executionBatchId, StringComparison.Ordinal))
            {
                Assert.AreEqual(HttpMethod.Patch, request.Method);
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                var transition = body.RootElement.GetProperty("transition").GetProperty("transition").GetString();
                if (transition == "Complete") completeCalls++;
                else Assert.AreEqual("Start", transition);
                return new(HttpStatusCode.OK);
            }
            if (path.Contains("/tasks/", StringComparison.Ordinal) && path.EndsWith("/leases", StringComparison.Ordinal))
            {
                var taskId = Guid.Parse(path.Split('/')[^2]);
                taskAcquireCalls.Add(taskId);
                var status = taskId == envelope.Tasks[1].TaskId
                    ? CollectionTaskAcquireStatus.ActiveElsewhere
                    : CollectionTaskAcquireStatus.AlreadyTerminal;
                return JsonResponse(new AcquireCollectionTaskResponse(new CollectionTaskAcquireResultDto(status)));
            }

            throw new AssertFailedException($"Unexpected worker request: {path}");
        });

        var response = await CollectionLambdaInvocation.ExecuteWakeAsync(WakeEvent("wake-active-elsewhere", wake), worker);

        CollectionAssert.AreEqual(envelope.Tasks.Select(x => x.TaskId).ToArray(), taskAcquireCalls);
        Assert.AreEqual(1, completeCalls);
        Assert.AreEqual("wake-active-elsewhere", response.BatchItemFailures.Single().ItemIdentifier);
    }

    [TestMethod]
    public async Task Wake_MalformedBodyIsTheOnlyRecordFailureAndLegacyEnvelopeIsAcknowledged()
    {
        var eventJson = JsonSerializer.Serialize(new
        {
            Records = new object[]
            {
                new { messageId = "malformed", body = "not-json" },
                new { messageId = "legacy", body = JsonSerializer.Serialize(Envelope(1), JsonOptions) },
            }
        });
        var worker = Worker(_ => throw new AssertFailedException("Legacy messages must not call acquire-next."));

        var response = await CollectionLambdaInvocation.ExecuteWakeAsync(eventJson, worker);

        Assert.AreEqual("malformed", response.BatchItemFailures.Single().ItemIdentifier);
    }

    private static CollectionDispatchEnvelope Envelope(int count) => new(Guid.NewGuid(),
        new("JRA", new("race-card"), new DateOnly(2026, 9, 12), CollectionLane.Realtime),
        Enumerable.Range(0, count).Select(_ => new CollectionDispatchTaskReference(Guid.NewGuid(), 1)).ToArray());

    private static CollectionExecutionAcquireResultDto ToWire(CollectionExecutionAcquireResult value)
        => new(value.Status, value.ExecutionBatchId, value.LeaseToken,
            value.Envelope is null ? null : new CollectionDispatchEnvelopeDto(value.Envelope.EnvelopeId,
                new CollectionDispatchCompatibilityKeyDto(value.Envelope.Compatibility.Provider,
                    new CollectionDefinitionIdDto(value.Envelope.Compatibility.Definition.Value),
                    value.Envelope.Compatibility.EffectiveDate, value.Envelope.Compatibility.Lane,
                    value.Envelope.Compatibility.GroupKind, value.Envelope.Compatibility.GroupKey),
                value.Envelope.Tasks.Select(x => new CollectionDispatchTaskReferenceDto(x.TaskId,
                    x.DispatchGeneration)).ToArray(), value.Envelope.ContractVersion),
            value.StartBefore, value.NoWorkReason, value.ReservationReleaseOutcome);

    private static string Event(string messageId, CollectionDispatchEnvelope envelope) => JsonSerializer.Serialize(new
    {
        Records = new[] { new { messageId, body = JsonSerializer.Serialize(envelope, JsonOptions) } },
    });

    private static string WakeEvent(string messageId, CollectionWakeSignal wake) => JsonSerializer.Serialize(new
    {
        Records = new[] { new { messageId, body = JsonSerializer.Serialize(wake, JsonOptions) } },
    });

    private static CollectionPlatformWorkerClient Worker(Func<HttpRequestMessage, HttpResponseMessage> response)
        => new(new HttpClient(new StubHandler(response)) { BaseAddress = new("https://api.test/") },
            new CollectionDefinitionHandlerRegistry([]));

    private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(value),
    };

    private static HttpResponseMessage JsonContentResponse<T>(HttpStatusCode status, T value) => new(status)
    {
        Content = JsonContent.Create(value, options: AcquireResponseJsonOptions),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
