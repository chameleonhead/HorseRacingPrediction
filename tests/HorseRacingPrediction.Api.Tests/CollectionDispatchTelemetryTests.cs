using Amazon.CloudWatch.Model;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionDispatchTelemetryTests
{
    [TestMethod]
    public async Task WakeSent_EmitsExactCounterNamesAndBoundedDimensions()
    {
        var publisher = new RecordingPublisher();
        var telemetry = CreateTelemetry(publisher, ["race-detail"]);

        await telemetry.RecordDispatchCycleAsync(CollectionDispatchCycleOutcome.WakeSent,
            CollectionLane.Realtime, "race-detail");
        await telemetry.RecordDispatchCycleAsync(CollectionDispatchCycleOutcome.WakeSendAmbiguousFailure,
            CollectionLane.Background, "https://resource-secret.example/race?token=credential-secret");

        var metrics = publisher.Metrics;
        Assert.IsTrue(metrics.Any(metric => metric.MetricName == "dispatch_cycle_total"
            && HasDimensions(metric, ("Outcome", "WakeSent"), ("Lane", "Realtime"), ("Definition", "race-detail"))));
        Assert.IsTrue(metrics.Any(metric => metric.MetricName == "wake_sent_total" && metric.Dimensions.Count == 0));
        Assert.IsTrue(metrics.Any(metric => metric.MetricName == "wake_sent_total"
            && HasDimensions(metric, ("Lane", "Realtime"), ("Definition", "race-detail"))));
        Assert.IsTrue(metrics.Any(metric => metric.MetricName == "queue_send_failure_total" && metric.Dimensions.Count == 0));
        Assert.IsTrue(metrics.Any(metric => metric.MetricName == "dispatch_cycle_total"
            && HasDimensions(metric, ("Outcome", "WakeSendAmbiguousFailure"), ("Lane", "Background"),
                ("Definition", "OTHER"))));
        Assert.IsFalse(metrics.SelectMany(metric => metric.Dimensions).Any(dimension =>
            dimension.Value.Contains("resource-secret", StringComparison.Ordinal)
            || dimension.Value.Contains("credential-secret", StringComparison.Ordinal)
            || dimension.Value.Contains("https://", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AcquireAndRelease_UseFiniteReasonAndOutcomeValues()
    {
        var publisher = new RecordingPublisher();
        var telemetry = CreateTelemetry(publisher, ["race-detail"]);

        await telemetry.RecordAcquireAsync(CollectionExecutionAcquireStatus.NoWork,
            CollectionExecutionNoWorkReason.ReservationInconsistent);
        await telemetry.RecordAcquireAsync(CollectionExecutionAcquireStatus.NoWork, null);
        await telemetry.RecordAcquireAsync(CollectionExecutionAcquireStatus.Acquired, null,
            CollectionLane.Normal, "unregistered-secret");
        await telemetry.RecordReservationReleaseAsync(CollectionReservationReleaseOutcome.SkippedStaleGeneration);

        Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "acquire_total"
            && HasDimensions(metric, ("Status", "NoWork"), ("Reason", "ReservationInconsistent"))));
        Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "acquire_total"
            && HasDimensions(metric, ("Status", "NoWork"), ("Reason", "Unknown"))));
        Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "acquire_nowork_total"
            && metric.Dimensions.Count == 0));
        Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "acquire_success_total"
            && HasDimensions(metric, ("Lane", "Normal"), ("Definition", "OTHER"))));
        Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "reservation_release_total"
            && HasDimensions(metric, ("Outcome", "SkippedStaleGeneration"))));
        Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "reservation_release_failure_total"
            && metric.Dimensions.Count == 0));
        Assert.IsFalse(publisher.Metrics.SelectMany(metric => metric.Dimensions)
            .Any(dimension => dimension.Value.Contains("secret", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Snapshot_EmitsTheApprovedGaugeSetAndLaneDefinitionSeries()
    {
        var publisher = new RecordingPublisher();
        var telemetry = CreateTelemetry(publisher, ["race-detail"]);
        var snapshot = new CollectionDispatchTelemetrySnapshot(1, 2, 3, 4, 5, 6,
        [
            new(CollectionLane.Background, "race-detail", 7, 8.5, 3, 4),
        ]);

        await telemetry.RecordSnapshotAsync(snapshot);

        CollectionAssert.AreEquivalent(new[]
        {
            "ready_missing_current_outbox", "outbox_cardinality_anomaly_tasks", "active_eligible_reservations",
            "expired_eligible_reservations", "in_flight_execution_leases", "max_in_flight_envelopes",
            "eligible_in_flight_count", "eligible_ready_rows", "oldest_eligible_age_seconds",
            "active_eligible_reservations_by_lane", "expired_eligible_reservations_by_lane",
        }, publisher.Metrics.Select(metric => metric.MetricName).ToArray());
        Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "oldest_eligible_age_seconds"
            && metric.Unit?.ToString() == "Seconds"
            && HasDimensions(metric, ("Lane", "Background"), ("Definition", "race-detail"))));
    }

    [TestMethod]
    public async Task Dispatcher_EmitsReservationAndWakeOutcomesOnTheRealStorePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dispatch-telemetry", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var publisher = new RecordingPublisher();
            var queueOptions = Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                Provider = "Sqs",
                DispatchBatchSize = 1,
                AggregationDelayMilliseconds = 0,
                TelemetryDefinitionLabels = ["race-detail"],
            });
            var telemetry = new CollectionDispatchTelemetry(publisher, queueOptions,
                NullLogger<CollectionDispatchTelemetry>.Instance);
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "resource-secret"),
                new("race-detail"), 1, CollectionReason.Initial, DateTimeOffset.UtcNow.AddMinutes(-1),
                lane: CollectionLane.Background);
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, new WakeOnlyQueue(), queueOptions,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance, telemetry);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "dispatch_cycle_total"
                && HasDimensions(metric, ("Outcome", "Reserved"), ("Lane", "Background"),
                    ("Definition", "race-detail"))));
            Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "wake_sent_total"
                && HasDimensions(metric, ("Lane", "Background"), ("Definition", "race-detail"))));
            Assert.IsFalse(publisher.Metrics.SelectMany(metric => metric.Dimensions)
                .Any(dimension => dimension.Value == "resource-secret"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task StoreSnapshot_ReportsEligibleRowsWithoutIdentifiers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dispatch-snapshot", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var now = DateTimeOffset.UtcNow;
            await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "resource-secret"),
                new("race-detail"), 1, CollectionReason.Initial, now.AddMinutes(-1),
                lane: CollectionLane.Background);

            var snapshot = await store.GetDispatchTelemetrySnapshotAsync(now, 1, ["race-detail"], 0);

            Assert.AreEqual(0, snapshot.ReadyMissingCurrentOutbox);
            Assert.AreEqual(1, snapshot.Lanes.Single().EligibleReadyRows);
            Assert.AreEqual(CollectionLane.Background, snapshot.Lanes.Single().Lane);
            Assert.AreEqual("race-detail", snapshot.Lanes.Single().DefinitionId);
            Assert.IsGreaterThan(0, snapshot.Lanes.Single().OldestEligibleAgeSeconds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CollectionDispatchTelemetry CreateTelemetry(RecordingPublisher publisher,
        List<string> definitionLabels)
        => new(publisher, Options.Create(new CollectionQueueOptions
        {
            Enabled = true,
            Provider = "Sqs",
            TelemetryDefinitionLabels = definitionLabels,
        }), NullLogger<CollectionDispatchTelemetry>.Instance);

    private static bool HasDimensions(MetricDatum metric, params (string Name, string Value)[] expected)
    {
        var dimensions = metric.Dimensions.ToDictionary(dimension => dimension.Name, dimension => dimension.Value);
        return dimensions.Count == expected.Length
            && expected.All(pair => dimensions.TryGetValue(pair.Name, out var value) && value == pair.Value);
    }

    private sealed class RecordingPublisher : ICollectionDispatchMetricPublisher
    {
        public List<MetricDatum> Metrics { get; } = [];

        public Task PublishAsync(IReadOnlyCollection<MetricDatum> metrics, CancellationToken cancellationToken)
        {
            Metrics.AddRange(metrics);
            return Task.CompletedTask;
        }
    }

    private sealed class WakeOnlyQueue : ICollectionPlatformTaskQueue
    {
        public Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake,
            CancellationToken cancellationToken) => Task.FromResult(new CollectionQueueSendReceipt("opaque-receipt"));
        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
