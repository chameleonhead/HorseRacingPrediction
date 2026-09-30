using Amazon.CloudWatch.Model;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionDispatchTelemetryTests
{
    [TestMethod]
    public async Task WakeSent_EmitsExactCounterNamesAndBoundedDimensions()
    {
        var publisher = new RecordingMetricQueue();
        var telemetry = CreateTelemetry(publisher, ["race-detail"]);

        await telemetry.RecordDispatchCycleAsync(CollectionDispatchCycleOutcome.WakeSent,
            CollectionLane.Realtime, "race-detail");
        await telemetry.RecordDispatchCycleAsync(CollectionDispatchCycleOutcome.WakeSendAmbiguousFailure,
            CollectionLane.Background, "https://resource-secret.example/race?token=credential-secret");
        await telemetry.RecordDispatchCycleAsync((CollectionDispatchCycleOutcome)int.MaxValue,
            CollectionLane.Background, "https://resource-secret.example/race?token=credential-secret");

        var metrics = publisher.Metrics;
        Assert.IsTrue(metrics.Any(metric => metric.MetricName == "dispatch_cycle_total"
            && HasDimensions(metric, ("Outcome", "WakeSent"))));
        Assert.IsTrue(metrics.Any(metric => metric.MetricName == "dispatch_cycle_by_lane_definition_total"
            && HasDimensions(metric, ("Outcome", "WakeSent"), ("Lane", "Realtime"), ("Definition", "race-detail"))));
        Assert.IsTrue(metrics.Any(metric => metric.MetricName == "wake_sent_total" && metric.Dimensions.Count == 0));
        Assert.IsTrue(metrics.Any(metric => metric.MetricName == "wake_sent_by_lane_definition_total"
            && HasDimensions(metric, ("Lane", "Realtime"), ("Definition", "race-detail"))));
        Assert.IsTrue(metrics.Any(metric => metric.MetricName == "queue_send_failure_total" && metric.Dimensions.Count == 0));
        Assert.IsTrue(metrics.Any(metric => metric.MetricName == "dispatch_cycle_by_lane_definition_total"
            && HasDimensions(metric, ("Outcome", "WakeSendAmbiguousFailure"), ("Lane", "Background"),
                ("Definition", "OTHER"))));
        Assert.IsTrue(metrics.Any(metric => metric.MetricName == "dispatch_cycle_total"
            && HasDimensions(metric, ("Outcome", "Unknown"))));
        Assert.IsFalse(metrics.SelectMany(metric => metric.Dimensions).Any(dimension =>
            dimension.Value.Contains("resource-secret", StringComparison.Ordinal)
            || dimension.Value.Contains("credential-secret", StringComparison.Ordinal)
            || dimension.Value.Contains("https://", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AcquireAndRelease_UseFiniteReasonAndOutcomeValues()
    {
        var publisher = new RecordingMetricQueue();
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
        Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "acquire_success_by_lane_definition_total"
            && HasDimensions(metric, ("Lane", "Normal"), ("Definition", "OTHER"))));
        Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "reservation_release_by_outcome_total"
            && HasDimensions(metric, ("Outcome", "SkippedStaleGeneration"))));
        Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "reservation_release_failure_total"
            && metric.Dimensions.Count == 0));
        Assert.IsFalse(publisher.Metrics.SelectMany(metric => metric.Dimensions)
            .Any(dimension => dimension.Value.Contains("secret", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task EveryTerraformMetricQuery_UsesAnExactlyEmittedDimensionSet()
    {
        var queue = new RecordingMetricQueue();
        var telemetry = CreateTelemetry(queue, ["race-detail"]);
        foreach (var outcome in Enum.GetValues<CollectionDispatchCycleOutcome>())
            await telemetry.RecordDispatchCycleAsync(outcome, CollectionLane.Realtime, "race-detail");
        await telemetry.RecordAcquireAsync(CollectionExecutionAcquireStatus.NoWork,
            CollectionExecutionNoWorkReason.InvalidRequest);
        await telemetry.RecordAcquireAsync(CollectionExecutionAcquireStatus.Acquired, null,
            CollectionLane.Realtime, "race-detail");
        foreach (var outcome in Enum.GetValues<CollectionReservationReleaseOutcome>())
            await telemetry.RecordReservationReleaseAsync(outcome);
        await telemetry.RecordLeaseReclaimedAsync();
        foreach (var status in new[] { CollectionTaskStatus.Succeeded, CollectionTaskStatus.Failed,
                     CollectionTaskStatus.Cancelled, CollectionTaskStatus.DeadLetter })
            await telemetry.RecordTerminalCompletionAsync(CollectionLane.Realtime, "race-detail", status);
        await telemetry.QueueSnapshotAsync(_ => Task.FromResult(new CollectionDispatchTelemetrySnapshot(
            1, 1, 1, 1, 1, 2, 2,
            [new(CollectionLane.Realtime, "race-detail", 1, 1, 1, 1)])));
        await queue.RunSnapshotsAsync();

        var emittedSchemas = queue.Metrics.Select(Schema).ToHashSet(StringComparer.Ordinal);
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "infra", "collector-lambda", "observability.tf"));
        var queriedSchemas = GetAlarmMetricSchemas(source).Concat(GetDashboardMetricSchemas(source)).ToArray();

        Assert.IsNotEmpty(queriedSchemas);
        foreach (var schema in queriedSchemas.Distinct(StringComparer.Ordinal))
            Assert.IsTrue(emittedSchemas.Contains(schema), $"No emitted metric matches Terraform query schema {schema}.");
    }

    [TestMethod]
    public async Task Snapshot_EmitsTheApprovedGaugeSetAndLaneDefinitionSeries()
    {
        var publisher = new RecordingMetricQueue();
        var telemetry = CreateTelemetry(publisher, ["race-detail"]);
        var snapshot = new CollectionDispatchTelemetrySnapshot(1, 2, 3, 4, 5, 6, 7,
        [
            new(CollectionLane.Background, "race-detail", 7, 8.5, 3, 4),
        ]);

        await telemetry.QueueSnapshotAsync(_ => Task.FromResult(snapshot));
        await publisher.RunSnapshotsAsync();

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
            var publisher = new RecordingMetricQueue();
            var queueOptions = Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                Provider = "Sqs",
                DispatchBatchSize = 1,
                AggregationDelayMilliseconds = 0,
                TelemetryDefinitionLabels = ["race-detail"],
            });
            var telemetry = new CollectionDispatchTelemetry(publisher, queueOptions);
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
            await publisher.RunSnapshotsAsync();

            Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "dispatch_cycle_by_lane_definition_total"
                && HasDimensions(metric, ("Outcome", "Reserved"), ("Lane", "Background"),
                    ("Definition", "race-detail"))));
            Assert.IsTrue(publisher.Metrics.Any(metric => metric.MetricName == "wake_sent_by_lane_definition_total"
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
    public async Task DispatcherAndAcquireTelemetry_ContinueWhenMetricQueueRejectsEveryWrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dispatch-telemetry-full", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var queueOptions = Options.Create(new CollectionQueueOptions
            {
                Enabled = true,
                Provider = "Sqs",
                DispatchBatchSize = 1,
                AggregationDelayMilliseconds = 0,
                TelemetryDefinitionLabels = ["race-detail"],
            });
            var queue = new RecordingMetricQueue { RejectWrites = true };
            var telemetry = new CollectionDispatchTelemetry(queue, queueOptions);
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "resource"), new("race-detail"), 1,
                CollectionReason.Initial, DateTimeOffset.UtcNow.AddMinutes(-1), lane: CollectionLane.Background);
            var wakeQueue = new WakeOnlyQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, wakeQueue, queueOptions,
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance, telemetry);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);
            await telemetry.RecordAcquireAsync(CollectionExecutionAcquireStatus.NoWork,
                CollectionExecutionNoWorkReason.ResourceUnavailable);
            await telemetry.RecordTerminalCompletionLookupAsync(_ => throw new InvalidOperationException());

            Assert.AreEqual(1, wakeQueue.WakesSent,
                "A full telemetry channel must not block or abort the actual SQS dispatch.");
        }
        finally { Directory.Delete(directory, recursive: true); }
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

    [TestMethod]
    public async Task StoreSnapshot_CountsDistinctEnvelopeSlotsAndLegacyDispatchedEnvelope()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dispatch-snapshot-slots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var now = DateTimeOffset.UtcNow;
            var receipts = new List<CollectionRequestReceipt>();
            foreach (var resourceId in new[] { "one", "two", "legacy-one", "legacy-two" })
                receipts.Add(await store.RequestAsync(new(CollectionResourceType.Race, "JRA", resourceId),
                    new("race-detail"), 1, CollectionReason.Initial, now.AddMinutes(-1), lane: CollectionLane.Background));

            var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False").Options;
            var sharedEnvelope = Guid.NewGuid();
            using (var db = new CollectionPlatformDbContext(options))
            {
                var ids = receipts.Where(x => x.TaskId.HasValue).Select(x => x.TaskId!.Value).ToArray();
                var rows = await db.DispatchOutbox.Where(x => ids.Contains(x.TaskId)).ToDictionaryAsync(x => x.TaskId);
                foreach (var taskId in ids.Take(2))
                {
                    var row = rows[taskId];
                    row.EnvelopeId = sharedEnvelope;
                    row.ReservedUntilUnixMilliseconds = now.AddMinutes(1).ToUnixTimeMilliseconds();
                }
                var legacyEnvelope = Guid.NewGuid();
                foreach (var taskId in ids.Skip(2))
                {
                    rows[taskId].EnvelopeId = legacyEnvelope;
                    rows[taskId].DispatchedAt = now;
                }
                await db.SaveChangesAsync();
            }

            var snapshot = await store.GetDispatchTelemetrySnapshotAsync(now, 4, ["race-detail"], 0);

            Assert.AreEqual(1, snapshot.ActiveEligibleReservations);
            Assert.AreEqual(2, snapshot.EligibleInFlightCount,
                "Capacity must count distinct reserved and legacy-dispatched envelope slots, not task rows.");
            Assert.AreEqual(1, snapshot.Lanes.Single().ActiveEligibleReservations);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task StoreSnapshot_CountsFutureReadyMissingOutboxOutsideDueEligibleGauge()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dispatch-snapshot-future", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", CollectionResourceType.Race,
                1, "initial", false);
            var now = DateTimeOffset.UtcNow;
            var receipt = await store.RequestAsync(new(CollectionResourceType.Race, "JRA", "future-ready"),
                new("race-detail"), 1, CollectionReason.Initial, now.AddHours(1), lane: CollectionLane.Background);
            var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False").Options;
            using (var db = new CollectionPlatformDbContext(options))
            {
                var taskId = receipt.TaskId!.Value;
                var task = await db.Tasks.SingleAsync(x => x.TaskId == taskId);
                var rows = db.DispatchOutbox.Where(x => x.TaskId == taskId);
                db.DispatchOutbox.RemoveRange(rows);
                task.AvailableAt = now.AddHours(1);
                await db.SaveChangesAsync();
            }

            var snapshot = await store.GetDispatchTelemetrySnapshotAsync(now, 4, ["race-detail"], 0);

            Assert.AreEqual(1, snapshot.ReadyMissingCurrentOutbox);
            Assert.AreEqual(0, snapshot.Lanes.Sum(x => x.EligibleReadyRows));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static CollectionDispatchTelemetry CreateTelemetry(RecordingMetricQueue publisher,
        List<string> definitionLabels)
        => new(publisher, Options.Create(new CollectionQueueOptions
        {
            Enabled = true,
            Provider = "Sqs",
            TelemetryDefinitionLabels = definitionLabels,
        }));

    private static bool HasDimensions(MetricDatum metric, params (string Name, string Value)[] expected)
    {
        var dimensions = metric.Dimensions.ToDictionary(dimension => dimension.Name, dimension => dimension.Value);
        return dimensions.Count == expected.Length
            && expected.All(pair => dimensions.TryGetValue(pair.Name, out var value) && value == pair.Value);
    }

    private static string Schema(MetricDatum metric)
        => metric.MetricName + "|" + string.Join(',', metric.Dimensions.Select(x => x.Name).Order(StringComparer.Ordinal));

    private static IEnumerable<string> GetAlarmMetricSchemas(string source)
    {
        foreach (Match match in Regex.Matches(source, "metric_name\\s*=\\s*\"(?<name>[^\"]+)\""))
        {
            var metricStart = source.LastIndexOf("metric {", match.Index, StringComparison.Ordinal);
            var queryStart = source.LastIndexOf("metric_query {", match.Index, StringComparison.Ordinal);
            var resourceStart = source.LastIndexOf("resource " + "\"aws_cloudwatch_metric_alarm\"", match.Index,
                StringComparison.Ordinal);
            if (metricStart < 0 || metricStart < queryStart || metricStart < resourceStart)
            {
                yield return match.Groups["name"].Value + "|";
                continue;
            }
            var open = source.IndexOf('{', metricStart);
            var end = FindClosingBrace(source, open);
            var block = source[metricStart..end];
            var dimensions = Regex.Match(block, "dimensions\\s*=\\s*\\{(?<body>[^}]*)\\}");
            IEnumerable<string> names = dimensions.Success
                ? Regex.Matches(dimensions.Groups["body"].Value, "(?<name>[A-Za-z][A-Za-z0-9_]*)\\s*=")
                    .Select(x => x.Groups["name"].Value).Order(StringComparer.Ordinal)
                : Array.Empty<string>();
            yield return match.Groups["name"].Value + "|" + string.Join(',', names);
        }
    }

    private static IEnumerable<string> GetDashboardMetricSchemas(string source)
    {
        foreach (var line in source.Split('\n').Where(line => line.Contains("local.collection_dispatch_namespace,", StringComparison.Ordinal)))
        {
            var values = Regex.Matches(line, "\"([^\"]*)\"").Select(x => x.Groups[1].Value).ToArray();
            if (values.Length == 0) continue;
            var dimensionNames = line.Contains("pair[0]", StringComparison.Ordinal)
                ? Regex.Matches(line, ",\\s*\"(?<name>[A-Za-z][A-Za-z0-9_]*)\"\\s*,\\s*pair\\[\\d+\\]")
                    .Select(x => x.Groups["name"].Value).Order(StringComparer.Ordinal)
                : values.Skip(1).Where((_, index) => index % 2 == 0).Order(StringComparer.Ordinal);
            yield return values[0] + "|" + string.Join(',', dimensionNames);
        }
    }

    private static int FindClosingBrace(string text, int open)
    {
        var depth = 0;
        for (var index = open; index < text.Length; index++)
        {
            if (text[index] == '{') depth++;
            else if (text[index] == '}' && --depth == 0) return index + 1;
        }
        throw new InvalidOperationException("Unclosed Terraform metric block.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HorseRacingPrediction.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class RecordingMetricQueue : ICollectionDispatchMetricQueue
    {
        public List<MetricDatum> Metrics { get; } = [];
        private readonly List<Func<CancellationToken, Task<IReadOnlyCollection<MetricDatum>>>> _snapshots = [];
        public bool RejectWrites { get; init; }

        public bool TryEnqueueMetrics(IReadOnlyCollection<MetricDatum> metrics)
        {
            if (RejectWrites) return false;
            Metrics.AddRange(metrics);
            return true;
        }

        public bool TryEnqueueSnapshot(Func<CancellationToken, Task<IReadOnlyCollection<MetricDatum>>> snapshot)
        {
            if (RejectWrites) return false;
            _snapshots.Add(snapshot);
            return true;
        }

        public async Task RunSnapshotsAsync()
        {
            foreach (var snapshot in _snapshots.ToArray()) Metrics.AddRange(await snapshot(CancellationToken.None));
            _snapshots.Clear();
        }
    }

    private sealed class WakeOnlyQueue : ICollectionPlatformTaskQueue
    {
        public int WakesSent { get; private set; }
        public Task<CollectionQueueSendReceipt> SendWakeAsync(CollectionWakeSignal wake,
            CancellationToken cancellationToken)
        {
            WakesSent++;
            return Task.FromResult(new CollectionQueueSendReceipt("opaque-receipt"));
        }
        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
