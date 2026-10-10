using Amazon.CloudWatch.Model;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Notifications;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionRuntimeStatusExecutionTests
{
    [TestMethod]
    public async Task WiredActions_ReportOnlyObservedWorkAcrossAllEightSlots()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-runtime-actions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        CollectionDispatchMetricQueue? metricQueue = null;
        var metricPublisher = new RecordingMetricPublisher();
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = directory,
            }));
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
                1, "Initial", false);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(store);
            services.AddSingleton<ICollectionSchedulePolicy, JraCollectionSchedulePolicy>();
            services.AddCollectionBackgroundSchedulers(enabled: true, watchdogEnabled: true,
                queueEnabled: true, deadLetterReconcilerEnabled: true,
                metricDeliveryEnabled: true, alertsEnabled: true);
            using var provider = services.BuildServiceProvider();
            var runtime = provider.GetRequiredService<CollectionRuntimeStatusRecorder>();
            var now = HorseRacingPrediction.Contracts.Common.Time.JstTime.Now();
            await provider.GetRequiredService<CollectionMaintenanceCoordinator>()
                .RunCycleAsync(CancellationToken.None);

            var queue = new RecordingTaskQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    AggregationDelayMilliseconds = 0,
                    MaxInFlightEnvelopes = 4,
                }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance, null, runtime);
            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            var hostedServices = provider.GetServices<IHostedService>();
            var watchdog = hostedServices.OfType<CollectionPlatformWatchdogService>().Single();
            await watchdog.RunOnceAsync(CancellationToken.None);

            var backfill = hostedServices.OfType<CollectionBackfillRecoveryService>().Single();
            await backfill.RunOnceAsync(CancellationToken.None);

            var reconciler = new CollectionPlatformDeadLetterReconciler(store, queue,
                Options.Create(new CollectionDeadLetterQueueReconcilerOptions()),
                NullLogger<CollectionPlatformDeadLetterReconciler>.Instance, runtime);
            await reconciler.RunOnceAsync(CancellationToken.None);

            var alerts = new CollectionPipelineAlertDispatchService(store, new RecordingAlertPublisher(),
                NullLogger<CollectionPipelineAlertDispatchService>.Instance, runtime);
            Assert.IsFalse(await alerts.RunOnceAsync(CancellationToken.None));

            metricQueue = new CollectionDispatchMetricQueue(metricPublisher,
                Options.Create(new CollectionDispatchMetricQueueOptions
                {
                    BatchWindow = TimeSpan.Zero,
                    PublishTimeout = TimeSpan.FromSeconds(1),
                    ShutdownDrainTimeout = TimeSpan.FromSeconds(1),
                }), NullLogger<CollectionDispatchMetricQueue>.Instance, runtime);
            await metricQueue.StartAsync(CancellationToken.None);
            Assert.IsTrue(metricQueue.TryEnqueueMetrics([new MetricDatum { MetricName = "accepted", Value = 1 }]));
            await metricPublisher.Published.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await metricQueue.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            var actions = runtime.GetSnapshot().Runtime.Actions.ToDictionary(x => x.Action);
            Assert.AreEqual(Enum.GetValues<CollectionRuntimeAction>().Length, actions.Count);
            Assert.AreEqual(1, actions[CollectionRuntimeAction.DiscoveryPlanner].CreatedCount);
            Assert.IsNotNull(actions[CollectionRuntimeAction.DiscoveryPlanner].LastProgressAtUtc);
            Assert.AreEqual(CollectionRuntimeReason.NoDueWork,
                actions[CollectionRuntimeAction.RefreshPlanner].Reason);
            Assert.AreEqual(1, actions[CollectionRuntimeAction.Dispatcher].SentCount);
            Assert.AreEqual(1, actions[CollectionRuntimeAction.Dispatcher].CompletedCount);
            Assert.IsNotNull(actions[CollectionRuntimeAction.Dispatcher].LastProgressAtUtc);
            Assert.AreEqual(CollectionRuntimeReason.NoDueWork, actions[CollectionRuntimeAction.TaskLeaseRecovery].Reason);
            Assert.AreEqual(CollectionRuntimeReason.NoDueWork, actions[CollectionRuntimeAction.BackfillRecovery].Reason);
            Assert.AreEqual(CollectionRuntimeReason.NoDueWork, actions[CollectionRuntimeAction.DeadLetterReconciliation].Reason);
            Assert.AreEqual(CollectionRuntimeReason.NoDueWork, actions[CollectionRuntimeAction.Alerts].Reason);
            Assert.AreEqual(1, actions[CollectionRuntimeAction.MetricDelivery].SentCount);
            Assert.AreEqual(1, actions[CollectionRuntimeAction.MetricDelivery].CompletedCount);
            Assert.IsNotNull(actions[CollectionRuntimeAction.MetricDelivery].LastProgressAtUtc);
        }
        finally
        {
            try
            {
                if (metricQueue is not null)
                    await metricQueue.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                metricQueue?.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task BackfillAdapter_ReportsBudgetStopWithoutProgress()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-runtime-budget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = directory,
            }));
            var clock = new BudgetElapsedTimeProvider(DateTimeOffset.Parse("2026-10-08T03:00:00Z"));
            var runtime = new CollectionRuntimeStatusRecorder(Enum.GetValues<CollectionRuntimeAction>()
                .Select(action => new CollectionRuntimeActionConfiguration(action, true,
                    action == CollectionRuntimeAction.MetricDelivery ? null : TimeSpan.FromMinutes(1))), clock);
            var service = new CollectionBackfillRecoveryService(store,
                NullLogger<CollectionBackfillRecoveryService>.Instance, clock, runtime);

            await service.RunOnceAsync(CancellationToken.None);

            var status = runtime.GetSnapshot().Runtime.Actions.Single(x =>
                x.Action == CollectionRuntimeAction.BackfillRecovery);
            Assert.AreEqual(CollectionRuntimeState.Waiting, status.State);
            Assert.AreEqual(CollectionRuntimeReason.BudgetExhausted, status.Reason);
            Assert.AreEqual(0, status.InspectedCount);
            Assert.AreEqual(0, status.CreatedCount);
            Assert.IsNull(status.LastProgressAtUtc);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingTaskQueue : ICollectionPlatformTaskQueue
    {
        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken) => Task.FromResult(new CollectionQueueSendReceipt("send-1"));

        public Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake,
            CancellationToken cancellationToken) => Task.FromResult(new CollectionQueueSendReceipt("wake-1"));
    }

    private sealed class RecordingAlertPublisher : ICollectionPipelineAlertPublisher
    {
        public Task PublishCollectionStoppedAsync(string reason, int dlqFailureCount,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingMetricPublisher : ICollectionDispatchMetricPublisher
    {
        public TaskCompletionSource Published { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PublishAsync(IReadOnlyCollection<MetricDatum> metrics, CancellationToken cancellationToken)
        {
            Published.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class BudgetElapsedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private int _timestampCalls;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override long GetTimestamp() => Interlocked.Increment(ref _timestampCalls) switch
        {
            1 or 2 => 0,
            _ => TimeSpan.FromSeconds(31).Ticks,
        };
    }
}
