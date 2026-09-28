using Amazon.CloudWatch.Model;
using HorseRacingPrediction.Api.CollectionController;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionDispatchMetricQueueTests
{
    [TestMethod]
    public void QueueFull_IsBoundedAndNonblocking()
    {
        var queue = CreateQueue(new RecordingPublisher(), capacity: 1);
        Assert.IsTrue(queue.TryEnqueueMetrics([Metric("first")]));
        Assert.IsFalse(queue.TryEnqueueMetrics([Metric("second")]));
    }

    [TestMethod]
    public async Task SnapshotQueryFailureAndPublisherFailure_AreSwallowedAndWorkerContinues()
    {
        var publisher = new RecordingPublisher { FailFirstPublish = true };
        var queue = CreateQueue(publisher, snapshotTimeoutMs: 50, batchWindowMs: 0);
        await queue.StartAsync(CancellationToken.None);
        Assert.IsTrue(queue.TryEnqueueSnapshot(_ => throw new InvalidOperationException("private details")));
        Assert.IsTrue(queue.TryEnqueueMetrics([Metric("still-attempted")]));
        await publisher.FirstPublish.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await queue.StopAsync(CancellationToken.None);
        Assert.AreEqual("still-attempted", publisher.Metrics.Single().MetricName);
    }

    [TestMethod]
    public async Task SnapshotTimeout_DoesNotPreventLaterBusinessMetricPublication()
    {
        var publisher = new RecordingPublisher();
        var queue = CreateQueue(publisher, snapshotTimeoutMs: 40, batchWindowMs: 0);
        await queue.StartAsync(CancellationToken.None);
        Assert.IsTrue(queue.TryEnqueueSnapshot(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        }));
        Assert.IsTrue(queue.TryEnqueueMetrics([Metric("after-timeout")]));
        await publisher.FirstPublish.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await queue.StopAsync(CancellationToken.None);
        Assert.AreEqual("after-timeout", publisher.Metrics.Single().MetricName);
    }

    [TestMethod]
    public async Task AcceptedMetrics_AreBatchedAndDrainedOnShutdown()
    {
        var publisher = new RecordingPublisher();
        var queue = CreateQueue(publisher, batchWindowMs: 1);
        Assert.IsTrue(queue.TryEnqueueMetrics([Metric("one")]));
        Assert.IsTrue(queue.TryEnqueueMetrics([Metric("two")]));
        await queue.StartAsync(CancellationToken.None);
        await publisher.FirstPublish.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await queue.StopAsync(CancellationToken.None);
        Assert.AreEqual(1, publisher.Batches);
        CollectionAssert.AreEquivalent(new[] { "one", "two" }, publisher.Metrics.Select(x => x.MetricName).ToArray());
    }

    [TestMethod]
    public async Task StuckPublisher_IsBoundedByTimeoutAndShutdownDrain()
    {
        var publisher = new RecordingPublisher { BlockPublish = true };
        var queue = CreateQueue(publisher, publishTimeoutMs: 40, shutdownTimeoutMs: 100, batchWindowMs: 0);
        await queue.StartAsync(CancellationToken.None);
        Assert.IsTrue(queue.TryEnqueueMetrics([Metric("bounded")]));
        await publisher.FirstPublish.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await queue.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        publisher.ReleasePublish.TrySetResult();
    }

    private static CollectionDispatchMetricQueue CreateQueue(RecordingPublisher publisher, int capacity = 8,
        int snapshotTimeoutMs = 200, int publishTimeoutMs = 200, int shutdownTimeoutMs = 500,
        int batchWindowMs = 10)
        => new(publisher, Options.Create(new CollectionDispatchMetricQueueOptions
        {
            Capacity = capacity,
            BatchWindow = TimeSpan.FromMilliseconds(batchWindowMs),
            SnapshotTimeout = TimeSpan.FromMilliseconds(snapshotTimeoutMs),
            PublishTimeout = TimeSpan.FromMilliseconds(publishTimeoutMs),
            ShutdownDrainTimeout = TimeSpan.FromMilliseconds(shutdownTimeoutMs),
        }), NullLogger<CollectionDispatchMetricQueue>.Instance);

    private static MetricDatum Metric(string name) => new() { MetricName = name, Value = 1 };

    private sealed class RecordingPublisher : ICollectionDispatchMetricPublisher
    {
        public List<MetricDatum> Metrics { get; } = [];
        public TaskCompletionSource FirstPublish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePublish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailFirstPublish { get; init; }
        public bool BlockPublish { get; init; }
        public int Batches { get; private set; }

        public async Task PublishAsync(IReadOnlyCollection<MetricDatum> metrics, CancellationToken cancellationToken)
        {
            Batches++;
            Metrics.AddRange(metrics);
            FirstPublish.TrySetResult();
            if (BlockPublish) await ReleasePublish.Task.ConfigureAwait(false);
            if (FailFirstPublish && Batches == 1) throw new InvalidOperationException("private details");
        }
    }
}
