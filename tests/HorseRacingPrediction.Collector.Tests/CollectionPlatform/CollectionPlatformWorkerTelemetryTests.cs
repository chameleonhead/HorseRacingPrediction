using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Http;
using Microsoft.Extensions.Logging;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionPlatformWorkerTelemetryTests
{
    [TestMethod]
    public async Task SuccessfulTask_EmitsOneSummaryWithAtLeastNinetyFivePercentCoverage()
    {
        var fixture = CreateFixture(HandlerBehavior.Success);

        await fixture.Client.ExecuteAsync(new(fixture.TaskId, 1), CancellationToken.None);

        var summary = AssertSingleSummary(fixture.Logger);
        Assert.AreEqual("horse-profile", summary["Definition"]);
        Assert.AreEqual(ResourceType.Horse, summary["ResourceType"]);
        Assert.AreEqual("Succeeded", summary["Result"]);
        Assert.IsInstanceOfType<int>(summary["MemorySizeMiB"]);
        Assert.IsGreaterThanOrEqualTo(0, (int)summary["MemorySizeMiB"]!);
        var total = Convert.ToDouble(summary["TotalMs"]);
        var attributed = Convert.ToDouble(summary["AcquireMs"])
                         + Convert.ToDouble(summary["HandlerMs"])
                         + Convert.ToDouble(summary["CompleteMs"]);
        Assert.IsGreaterThanOrEqualTo(0.95, attributed / total);
        Assert.AreEqual(0d, Convert.ToDouble(summary["UnattributedMs"]));
    }

    [TestMethod]
    public async Task HandlerInternalApi_IsAttributedWithoutChangingTheTerminalEvent()
    {
        var fixture = CreateFixture(HandlerBehavior.Success, includeHandlerApi: true);

        await fixture.Client.ExecuteAsync(new(fixture.TaskId, 1), CancellationToken.None);

        var summary = AssertSingleSummary(fixture.Logger);
        Assert.AreEqual(15d, Convert.ToDouble(summary["HandlerApiMs"]));
        Assert.AreEqual(1, summary["HandlerApiCallCount"]);
        Assert.AreEqual(45d, Convert.ToDouble(summary["HandlerNonApiMs"]));
        Assert.AreEqual(60d, Convert.ToDouble(summary["HandlerMs"]));
        CollectionAssert.AreEquivalent(new[]
        {
            "Definition", "ResourceType", "Result", "MemorySizeMiB", "TotalMs", "AcquireMs", "HandlerMs",
            "HandlerApiMs", "HandlerApiCallCount", "HandlerNonApiMs", "CompleteMs", "UnattributedMs",
        }, summary.Keys.ToArray());
    }

    [TestMethod]
    public async Task HandlerInternalApiFailure_IsMeasuredAndOriginalFailureClassificationIsPreserved()
    {
        var fixture = CreateFixture(HandlerBehavior.Success, includeHandlerApi: true, handlerApiFailure: true);

        await fixture.Client.ExecuteAsync(new(fixture.TaskId, 1), CancellationToken.None);

        var summary = AssertSingleSummary(fixture.Logger);
        Assert.AreEqual("TransientFailure", summary["Result"]);
        Assert.AreEqual(15d, Convert.ToDouble(summary["HandlerApiMs"]));
        Assert.AreEqual(1, summary["HandlerApiCallCount"]);
        Assert.AreEqual(45d, Convert.ToDouble(summary["HandlerNonApiMs"]));
    }

    [TestMethod]
    public async Task TimingHandler_CountsOneLogicalCallAcrossRetries()
    {
        var clock = new ManualTelemetryClock();
        using var scope = CollectionRuntimeTimingContext.BeginTask();
        using var client = new HttpClient(new CollectionRuntimeTimingHandler(clock)
        {
            InnerHandler = new TransientBadGatewayRetryHandler(TimeSpan.Zero)
            {
                InnerHandler = new RetryingTransport(clock),
            },
        })
        { BaseAddress = new("https://api.test/") };

        using var response = await client.GetAsync("fixed-operation");

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        var timing = scope.Accumulator.Snapshot();
        Assert.AreEqual(10d, timing.ApiElapsed.TotalMilliseconds);
        Assert.AreEqual(1, timing.ApiCallCount);
    }

    [TestMethod]
    public async Task FailedTask_EmitsOneSafeTerminalSummary()
    {
        var fixture = CreateFixture(HandlerBehavior.Failure);

        await fixture.Client.ExecuteAsync(new(fixture.TaskId, 1), CancellationToken.None);

        var summary = AssertSingleSummary(fixture.Logger);
        Assert.AreEqual("AccessLimited", summary["Result"]);
        Assert.AreEqual(0d, Convert.ToDouble(summary["HandlerApiMs"]));
        Assert.AreEqual(0, summary["HandlerApiCallCount"]);
        Assert.AreEqual(Convert.ToDouble(summary["HandlerMs"]), Convert.ToDouble(summary["HandlerNonApiMs"]));
        AssertSafe(fixture.Logger.RenderedMessages.Single());
    }

    [TestMethod]
    public async Task CancelledTask_EmitsOneSafeTerminalSummary()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = CreateFixture(HandlerBehavior.Cancellation, cancellation);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Client.ExecuteAsync(new(fixture.TaskId, 1), cancellation.Token));

        var summary = AssertSingleSummary(fixture.Logger);
        Assert.AreEqual("TransientFailure", summary["Result"]);
        Assert.AreEqual(0d, Convert.ToDouble(summary["HandlerApiMs"]));
        Assert.AreEqual(0, summary["HandlerApiCallCount"]);
        Assert.AreEqual(Convert.ToDouble(summary["HandlerMs"]), Convert.ToDouble(summary["HandlerNonApiMs"]));
        AssertSafe(fixture.Logger.RenderedMessages.Single());
    }

    private static Fixture CreateFixture(HandlerBehavior behavior, CancellationTokenSource? cancellation = null,
        bool includeHandlerApi = false, bool handlerApiFailure = false)
    {
        var taskId = Guid.NewGuid();
        var clock = new ManualTelemetryClock();
        var lease = new LeasedCollectionTask(taskId, Guid.NewGuid(), new(ResourceType.Horse, "JRA", "resource-secret"),
            new("horse-profile"), 1, CollectionReason.Initial, CollectionLane.Normal, 50,
            "lease-secret", DateTimeOffset.UtcNow.AddMinutes(15), null,
            new Dictionary<string, string> { ["payload"] = "payload-secret" });
        var transport = new TimedTransport(lease, clock);
        HttpClient? handlerClient = null;
        if (includeHandlerApi)
        {
            handlerClient = new HttpClient(new CollectionRuntimeTimingHandler(clock)
            {
                InnerHandler = new TimedApiTransport(clock, handlerApiFailure),
            })
            { BaseAddress = new("https://api.test/") };
        }
        var handler = new TimedHandler(behavior, clock, cancellation, handlerClient);
        var logger = new CapturingLogger();
        var client = new CollectionPlatformWorkerClient(
            new HttpClient(transport) { BaseAddress = new("https://api.test/") },
            new CollectionDefinitionHandlerRegistry([handler]), logger, clock);
        return new(taskId, client, logger);
    }

    private static IReadOnlyDictionary<string, object?> AssertSingleSummary(CapturingLogger logger)
    {
        Assert.HasCount(1, logger.Entries);
        return logger.Entries.Single();
    }

    private static void AssertSafe(string message)
    {
        Assert.DoesNotContain("resource-secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("lease-secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("payload-secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("handler-error-secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", message, StringComparison.Ordinal);
    }

    private sealed record Fixture(Guid TaskId, CollectionPlatformWorkerClient Client, CapturingLogger Logger);

    private enum HandlerBehavior { Success, Failure, Cancellation }

    private sealed class TimedHandler(HandlerBehavior behavior, ManualTelemetryClock clock,
        CancellationTokenSource? cancellation, HttpClient? client) : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new("horse-profile");
        public ResourceType ResourceType => ResourceType.Horse;

        public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken token)
        {
            clock.Advance(TimeSpan.FromMilliseconds(45));
            if (behavior == HandlerBehavior.Failure)
                throw new HttpRequestException("handler-error-secret", null, HttpStatusCode.TooManyRequests);
            if (behavior == HandlerBehavior.Cancellation)
            {
                cancellation!.Cancel();
                await Task.FromCanceled(token);
            }
            if (client is not null)
                using (await client.GetAsync("api/internal/fixed-operation", token)) { }
            return new CollectionAttemptCompletion(CollectionAttemptResult.Succeeded);
        }
    }

    private sealed class TimedApiTransport(ManualTelemetryClock clock, bool fail) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            clock.Advance(TimeSpan.FromMilliseconds(15));
            if (fail) throw new HttpRequestException("api-error-secret", null, HttpStatusCode.ServiceUnavailable);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class RetryingTransport(ManualTelemetryClock clock) : HttpMessageHandler
    {
        private int _calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            clock.Advance(TimeSpan.FromMilliseconds(5));
            var status = Interlocked.Increment(ref _calls) == 1
                ? HttpStatusCode.BadGateway : HttpStatusCode.NoContent;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private sealed class TimedTransport(LeasedCollectionTask lease, ManualTelemetryClock clock) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/acquire", StringComparison.Ordinal))
            {
                clock.Advance(TimeSpan.FromMilliseconds(20));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new CollectionTaskAcquireResult(
                        CollectionTaskAcquireStatus.Acquired, lease)),
                });
            }
            clock.Advance(TimeSpan.FromMilliseconds(30));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class ManualTelemetryClock : ICollectionTaskTelemetryClock
    {
        private long _milliseconds;
        public long GetTimestamp() => _milliseconds;
        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
            => TimeSpan.FromMilliseconds(endingTimestamp - startingTimestamp);
        public void Advance(TimeSpan value) => _milliseconds += (long)value.TotalMilliseconds;
    }

    private sealed class CapturingLogger : ILogger<CollectionPlatformWorkerClient>
    {
        public List<IReadOnlyDictionary<string, object?>> Entries { get; } = [];
        public List<string> RenderedMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Information || state is not IEnumerable<KeyValuePair<string, object?>> values)
                return;
            Entries.Add(values.Where(x => x.Key != "{OriginalFormat}").ToDictionary(x => x.Key, x => x.Value));
            RenderedMessages.Add(formatter(state, exception));
        }
    }
}
