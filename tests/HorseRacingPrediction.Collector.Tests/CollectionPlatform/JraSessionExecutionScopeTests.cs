using HorseRacingPrediction.Collector.CollectionPlatform;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Parsing;

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
}
