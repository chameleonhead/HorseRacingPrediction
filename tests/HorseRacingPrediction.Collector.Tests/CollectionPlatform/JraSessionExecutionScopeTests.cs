using System.Net;
using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Http;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using Microsoft.Extensions.Logging;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class JraSessionExecutionScopeTests
{
    [TestMethod]
    public async Task AcquireOutsideBatch_OwnsAndDisposesEachSession()
    {
        var factory = new TrackingSessionFactory();

        await using (var first = await JraSessionExecutionScope.AcquireAsync(factory, CancellationToken.None))
            Assert.IsFalse(factory.Browsers[0].IsDisposed);
        await using (var second = await JraSessionExecutionScope.AcquireAsync(factory, CancellationToken.None))
            Assert.IsFalse(factory.Browsers[1].IsDisposed);

        Assert.AreEqual(2, factory.CreateCallCount);
        Assert.IsTrue(factory.Browsers.All(x => x.IsDisposed));
    }

    [TestMethod]
    public async Task ExecuteBatch_ReusesSessionAndDisposesItOnceAfterBatch()
    {
        var factory = new TrackingSessionFactory();
        JraSession? firstSession = null;

        await JraSessionExecutionScope.ExecuteAsync(factory, async cancellationToken =>
        {
            await using var first = await JraSessionExecutionScope.AcquireAsync(factory, cancellationToken);
            firstSession = first.Session;
            await using var second = await JraSessionExecutionScope.AcquireAsync(factory, cancellationToken);

            Assert.AreSame(first.Session, second.Session);
            Assert.IsFalse(factory.Browsers.Single().IsDisposed);
        }, CancellationToken.None);

        Assert.IsNotNull(firstSession);
        Assert.AreEqual(1, factory.CreateCallCount);
        Assert.IsTrue(factory.Browsers.Single().IsDisposed);
        Assert.AreEqual(1, factory.Browsers.Single().DisposeCallCount);
    }

    [TestMethod]
    public async Task MeasuredExecution_LogsApiAndNonApiTimeOnceAfterDisposal()
    {
        var factory = new TrackingSessionFactory();
        var clock = new ManualClock();
        var logger = new CapturingLogger(() => factory.Browsers.SingleOrDefault()?.IsDisposed == true);
        using var client = new HttpClient(new CollectionRuntimeTimingHandler(clock)
        {
            InnerHandler = new TimedTransport(clock),
        })
        { BaseAddress = new("https://api.test/") };

        await JraSessionExecutionScope.ExecuteMeasuredAsync(factory, async cancellationToken =>
        {
            clock.Advance(10);
            await using var lease = await JraSessionExecutionScope.AcquireAsync(factory, cancellationToken);
            using (await client.GetAsync("secret/path/value", cancellationToken)) { }
            clock.Advance(5);
        }, CancellationToken.None,
            new("JRA", new("race-detail"), null, CollectionLane.Normal,
                CollectionDispatchGroupKind.RaceDay, "secret-group"),
            12, logger, clock);

        Assert.HasCount(1, logger.Entries);
        var entry = logger.Entries.Single();
        Assert.AreEqual("RaceDay", entry["GroupKind"]);
        Assert.AreEqual("Succeeded", entry["Result"]);
        Assert.AreEqual(12, entry["TaskCount"]);
        Assert.AreEqual(true, entry["BrowserCreated"]);
        Assert.AreEqual(35d, Convert.ToDouble(entry["SessionMs"]));
        Assert.AreEqual(20d, Convert.ToDouble(entry["SessionApiMs"]));
        Assert.AreEqual(1, entry["SessionApiCallCount"]);
        Assert.AreEqual(15d, Convert.ToDouble(entry["SessionNonApiMs"]));
        Assert.IsTrue(factory.Browsers.Single().IsDisposed);
        Assert.IsTrue(logger.ConditionWhenLogged);
        Assert.DoesNotContain("secret", logger.RenderedMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", logger.RenderedMessage, StringComparison.Ordinal);
        CollectionAssert.AreEquivalent(new[]
        {
            "GroupKind", "Result", "TaskCount", "BrowserCreated", "SessionMs", "SessionApiMs",
            "SessionApiCallCount", "SessionNonApiMs",
        }, entry.Keys.ToArray());
    }

    [TestMethod]
    public async Task MeasuredExecution_WithoutBrowser_LogsOneSafeSummary()
    {
        var logger = new CapturingLogger();
        var clock = new ManualClock();

        await JraSessionExecutionScope.ExecuteMeasuredAsync(new TrackingSessionFactory(),
            _ => Task.CompletedTask, CancellationToken.None, null, 0, logger, clock);

        Assert.HasCount(1, logger.Entries);
        Assert.AreEqual(false, logger.Entries.Single()["BrowserCreated"]);
        Assert.AreEqual("Succeeded", logger.Entries.Single()["Result"]);
        Assert.AreEqual(0, logger.Entries.Single()["SessionApiCallCount"]);
    }

    [TestMethod]
    public async Task MeasuredExecution_FailureAndCancellation_EachLogOneTerminalSummary()
    {
        var failureLogger = new CapturingLogger();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JraSessionExecutionScope.ExecuteMeasuredAsync(new TrackingSessionFactory(),
                _ => throw new InvalidOperationException("secret-error"), CancellationToken.None,
                null, 1, failureLogger, new ManualClock()));
        Assert.HasCount(1, failureLogger.Entries);
        Assert.AreEqual("Failed", failureLogger.Entries.Single()["Result"]);
        Assert.DoesNotContain("secret-error", failureLogger.RenderedMessage, StringComparison.Ordinal);

        var cancellationLogger = new CapturingLogger();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            JraSessionExecutionScope.ExecuteMeasuredAsync(new TrackingSessionFactory(),
                _ => throw new OperationCanceledException("secret-cancellation"), CancellationToken.None,
                null, 1, cancellationLogger, new ManualClock()));
        Assert.HasCount(1, cancellationLogger.Entries);
        Assert.AreEqual("Cancelled", cancellationLogger.Entries.Single()["Result"]);
        Assert.DoesNotContain("secret-cancellation", cancellationLogger.RenderedMessage, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task MeasuredExecution_DisposeFailure_StillLogsOnceAfterDisposeAttempt()
    {
        var factory = new ThrowingDisposeSessionFactory();
        var logger = new CapturingLogger(() => factory.Browser.DisposeAttempted);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JraSessionExecutionScope.ExecuteMeasuredAsync(factory, async cancellationToken =>
            {
                await using var lease = await JraSessionExecutionScope.AcquireAsync(factory, cancellationToken);
            }, CancellationToken.None, null, 1, logger, new ManualClock()));

        Assert.HasCount(1, logger.Entries);
        Assert.AreEqual("Failed", logger.Entries.Single()["Result"]);
        Assert.IsTrue(logger.ConditionWhenLogged);
    }

    [TestMethod]
    public async Task ParallelTimingScopes_DoNotMixApiCounts()
    {
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        async Task<CollectionRuntimeTimingContext.TimingSnapshot> MeasureAsync(int calls)
        {
            using var scope = CollectionRuntimeTimingContext.BeginSession();
            if (Interlocked.Increment(ref started) == 2) bothStarted.SetResult();
            await bothStarted.Task;
            for (var index = 0; index < calls; index++)
                CollectionRuntimeTimingContext.RecordInternalApi(TimeSpan.FromMilliseconds(3));
            return scope.Accumulator.Snapshot();
        }

        var results = await Task.WhenAll(Task.Run(() => MeasureAsync(1)), Task.Run(() => MeasureAsync(2)));

        CollectionAssert.AreEquivalent(new[] { 1, 2 }, results.Select(x => x.ApiCallCount).ToArray());
        CollectionAssert.AreEquivalent(new[] { 3d, 6d },
            results.Select(x => x.ApiElapsed.TotalMilliseconds).ToArray());
    }

    [TestMethod]
    public async Task ParallelBatchExecutions_HaveIsolatedAsyncLocalSessions()
    {
        var factory = new TrackingSessionFactory();
        var bothAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquiredCount = 0;
        var sessions = new JraSession?[2];

        async Task RunAsync(int index)
        {
            await JraSessionExecutionScope.ExecuteAsync(factory, async cancellationToken =>
            {
                await using var lease = await JraSessionExecutionScope.AcquireAsync(factory, cancellationToken);
                sessions[index] = lease.Session;
                if (Interlocked.Increment(ref acquiredCount) == 2) bothAcquired.SetResult();
                await bothAcquired.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }, CancellationToken.None);
        }

        await Task.WhenAll(Task.Run(() => RunAsync(0)), Task.Run(() => RunAsync(1)));

        Assert.AreEqual(2, factory.CreateCallCount);
        Assert.AreNotSame(sessions[0], sessions[1]);
        Assert.IsTrue(factory.Browsers.All(x => x.IsDisposed && x.DisposeCallCount == 1));
    }

    [TestMethod]
    public async Task SessionConstructionFailure_DoesNotLeakAmbientScopeIntoNextExecution()
    {
        var failing = new ThrowingSessionFactory();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            JraSessionExecutionScope.ExecuteAsync(failing, async cancellationToken =>
            {
                await using var lease = await JraSessionExecutionScope.AcquireAsync(failing, cancellationToken);
            }, CancellationToken.None));

        var healthy = new TrackingSessionFactory();
        await using var standalone = await JraSessionExecutionScope.AcquireAsync(healthy, CancellationToken.None);

        Assert.AreEqual(1, healthy.CreateCallCount);
    }

    private sealed class TrackingSessionFactory : IJraSessionFactory
    {
        private int _createCallCount;
        public int CreateCallCount => _createCallCount;
        public List<TrackingBrowser> Browsers { get; } = [];

        public Task<JraSession> CreateAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _createCallCount);
            var browser = new TrackingBrowser();
            lock (Browsers) Browsers.Add(browser);
            return Task.FromResult(new JraSession(browser, new FakeJraNavigator(),
                new JraPageReader(browser, Array.Empty<IJraPageParser>())));
        }
    }

    private sealed class TrackingBrowser : NoOpWebBrowser
    {
        public int DisposeCallCount { get; private set; }

        public override ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return base.DisposeAsync();
        }
    }

    private sealed class ThrowingSessionFactory : IJraSessionFactory
    {
        public Task<JraSession> CreateAsync(CancellationToken cancellationToken = default)
            => Task.FromException<JraSession>(new InvalidOperationException("Browser initialization failed."));
    }

    private sealed class ThrowingDisposeSessionFactory : IJraSessionFactory
    {
        public ThrowingDisposeBrowser Browser { get; } = new();
        public Task<JraSession> CreateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new JraSession(Browser, new FakeJraNavigator(),
                new JraPageReader(Browser, Array.Empty<IJraPageParser>())));
    }

    private sealed class ThrowingDisposeBrowser : NoOpWebBrowser
    {
        public bool DisposeAttempted { get; private set; }
        public override ValueTask DisposeAsync()
        {
            DisposeAttempted = true;
            throw new InvalidOperationException("dispose-error-secret");
        }
    }

    private sealed class ManualClock : ICollectionTaskTelemetryClock
    {
        private long _milliseconds;
        public long GetTimestamp() => _milliseconds;
        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
            => TimeSpan.FromMilliseconds(endingTimestamp - startingTimestamp);
        public void Advance(long milliseconds) => _milliseconds += milliseconds;
    }

    private sealed class TimedTransport(ManualClock clock) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            clock.Advance(20);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class CapturingLogger(Func<bool>? condition = null) : ILogger
    {
        public List<IReadOnlyDictionary<string, object?>> Entries { get; } = [];
        public string RenderedMessage { get; private set; } = string.Empty;
        public bool ConditionWhenLogged { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Information || state is not IEnumerable<KeyValuePair<string, object?>> values)
                return;
            Entries.Add(values.Where(x => x.Key != "{OriginalFormat}").ToDictionary(x => x.Key, x => x.Value));
            RenderedMessage = formatter(state, exception);
            ConditionWhenLogged = condition?.Invoke() ?? true;
        }
    }
}
