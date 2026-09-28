using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.CollectionController;

public interface ICollectionDispatchMetricPublisher
{
    Task PublishAsync(IReadOnlyCollection<MetricDatum> metrics, CancellationToken cancellationToken);
}

public sealed class NullCollectionDispatchTelemetry : ICollectionDispatchTelemetry
{
    public Task RecordDispatchCycleAsync(CollectionDispatchCycleOutcome outcome, CollectionLane? lane = null,
        string? definitionId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RecordAcquireAsync(CollectionExecutionAcquireStatus status, CollectionExecutionNoWorkReason? reason,
        CollectionLane? lane = null, string? definitionId = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    public Task RecordReservationReleaseAsync(CollectionReservationReleaseOutcome outcome,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RecordLeaseReclaimedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RecordTerminalCompletionAsync(CollectionLane lane, string definitionId, CollectionTaskStatus status,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RecordSnapshotAsync(CollectionDispatchTelemetrySnapshot snapshot,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class CloudWatchCollectionDispatchMetricPublisher(IAmazonCloudWatch cloudWatch)
    : ICollectionDispatchMetricPublisher
{
    public async Task PublishAsync(IReadOnlyCollection<MetricDatum> metrics, CancellationToken cancellationToken)
    {
        if (metrics.Count == 0) return;
        await cloudWatch.PutMetricDataAsync(new PutMetricDataRequest
        {
            Namespace = CollectionDispatchTelemetry.Namespace,
            MetricData = metrics.ToList(),
        }, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class CollectionDispatchTelemetry(
    ICollectionDispatchMetricPublisher publisher,
    IOptions<CollectionQueueOptions> queueOptions,
    ILogger<CollectionDispatchTelemetry> logger) : ICollectionDispatchTelemetry
{
    public const string Namespace = "HorseRacingPrediction/CollectionDispatch";
    private long _lastSnapshotUtcTicks;

    private static readonly string[] DefinitionAllowlist = ["race-discovery", "race-detail", "race-odds"];
    private readonly HashSet<string> _definitionLabels = (queueOptions.Value.TelemetryDefinitionLabels.Count == 0
            ? (IEnumerable<string>)DefinitionAllowlist
            : queueOptions.Value.TelemetryDefinitionLabels)
        .Where(IsSafeDefinitionLabel)
        .Take(16)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private readonly bool _enabled = queueOptions.Value.Enabled
        && string.Equals(queueOptions.Value.Provider, "Sqs", StringComparison.OrdinalIgnoreCase);

    public Task RecordDispatchCycleAsync(CollectionDispatchCycleOutcome outcome, CollectionLane? lane = null,
        string? definitionId = null, CancellationToken cancellationToken = default)
    {
        var dimensions = new List<Dimension> { Dimension("Outcome", outcome.ToString()) };
        AddLaneDefinition(dimensions, lane, definitionId);
        if (outcome == CollectionDispatchCycleOutcome.WakeSent)
        {
            var throughputDimensions = new List<Dimension>();
            AddLaneDefinition(throughputDimensions, lane, definitionId);
            return PublishAsync(
            [
                CountMetric("dispatch_cycle_total", dimensions),
                CountMetric("wake_sent_total", []),
                CountMetric("wake_sent_total", throughputDimensions),
            ], cancellationToken);
        }
        if (outcome is CollectionDispatchCycleOutcome.WakeSendDefiniteFailure
            or CollectionDispatchCycleOutcome.WakeSendAmbiguousFailure)
            return PublishAsync(
            [
                CountMetric("dispatch_cycle_total", dimensions),
                CountMetric("queue_send_failure_total", []),
            ], cancellationToken);
        return PublishCounterAsync("dispatch_cycle_total", dimensions, cancellationToken);
    }

    public Task RecordAcquireAsync(CollectionExecutionAcquireStatus status, CollectionExecutionNoWorkReason? reason,
        CollectionLane? lane = null, string? definitionId = null, CancellationToken cancellationToken = default)
    {
        var boundedReason = status == CollectionExecutionAcquireStatus.Acquired
            ? "Acquired"
            : reason is { } value && Enum.IsDefined(value) ? value.ToString() : "Unknown";
        var metrics = new List<MetricDatum>
        {
            CountMetric("acquire_total", [Dimension("Status", status.ToString()), Dimension("Reason", boundedReason)]),
        };
        if (status == CollectionExecutionAcquireStatus.NoWork)
            metrics.Add(CountMetric("acquire_nowork_total", []));
        if (status == CollectionExecutionAcquireStatus.Acquired)
        {
            var throughputDimensions = new List<Dimension>();
            AddLaneDefinition(throughputDimensions, lane, definitionId);
            metrics.Add(CountMetric("acquire_success_total", []));
            metrics.Add(CountMetric("acquire_success_total", throughputDimensions));
        }
        return PublishAsync(metrics, cancellationToken);
    }

    public Task RecordReservationReleaseAsync(CollectionReservationReleaseOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        var metrics = new List<MetricDatum>
        {
            CountMetric("reservation_release_total", [Dimension("Outcome", outcome.ToString())]),
            CountMetric("reservation_release_total", []),
        };
        if (outcome != CollectionReservationReleaseOutcome.Released)
            metrics.Add(CountMetric("reservation_release_failure_total", []));
        return PublishAsync(metrics, cancellationToken);
    }

    public Task RecordLeaseReclaimedAsync(CancellationToken cancellationToken = default)
        => PublishCounterAsync("execution_lease_reclaimed_total", [Dimension("Reason", "Expired")], cancellationToken);

    public Task RecordTerminalCompletionAsync(CollectionLane lane, string definitionId, CollectionTaskStatus status,
        CancellationToken cancellationToken = default)
    {
        if (status is not (CollectionTaskStatus.Succeeded or CollectionTaskStatus.Failed
            or CollectionTaskStatus.Cancelled or CollectionTaskStatus.DeadLetter)) return Task.CompletedTask;
        var dimensions = new List<Dimension> { Dimension("TaskStatus", status.ToString()) };
        AddLaneDefinition(dimensions, lane, definitionId);
        return PublishAsync(
        [
            CountMetric("terminal_task_completion_total", []),
            CountMetric("terminal_task_completion_total", dimensions),
        ], cancellationToken);
    }

    public async Task RecordSnapshotAsync(CollectionDispatchTelemetrySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var previous = Interlocked.Read(ref _lastSnapshotUtcTicks);
        if (previous != 0 && nowTicks - previous < TimeSpan.TicksPerMinute) return;
        if (Interlocked.CompareExchange(ref _lastSnapshotUtcTicks, nowTicks, previous) != previous) return;
        var metrics = new List<MetricDatum>
        {
            Gauge("ready_missing_current_outbox", snapshot.ReadyMissingCurrentOutbox),
            Gauge("outbox_cardinality_anomaly_tasks", snapshot.CardinalityAnomalyTasks),
            Gauge("active_eligible_reservations", snapshot.ActiveEligibleReservations),
            Gauge("expired_eligible_reservations", snapshot.ExpiredEligibleReservations),
            Gauge("in_flight_execution_leases", snapshot.InFlightExecutionLeases),
            Gauge("max_in_flight_envelopes", Math.Max(1, snapshot.MaxInFlightEnvelopes)),
            Gauge("eligible_in_flight_count", snapshot.ActiveEligibleReservations + snapshot.InFlightExecutionLeases),
        };
        foreach (var lane in snapshot.Lanes)
        {
            var dimensions = new List<Dimension>();
            AddLaneDefinition(dimensions, lane.Lane, lane.DefinitionId);
            metrics.Add(Gauge("eligible_ready_rows", lane.EligibleReadyRows, dimensions));
            metrics.Add(Gauge("oldest_eligible_age_seconds", lane.OldestEligibleAgeSeconds, dimensions,
                StandardUnit.Seconds));
            metrics.Add(Gauge("active_eligible_reservations_by_lane", lane.ActiveEligibleReservations, dimensions));
            metrics.Add(Gauge("expired_eligible_reservations_by_lane", lane.ExpiredEligibleReservations, dimensions));
        }
        await PublishAsync(metrics, cancellationToken).ConfigureAwait(false);
    }

    internal async Task PublishAsync(IReadOnlyCollection<MetricDatum> metrics, CancellationToken cancellationToken)
    {
        if (!_enabled) return;
        try
        {
            await publisher.PublishAsync(metrics, cancellationToken).ConfigureAwait(false);
            foreach (var metric in metrics)
                logger.LogInformation("Collection dispatch metric emitted {MetricName} {MetricValue} {Dimensions}",
                    metric.MetricName, metric.Value, string.Join(',', metric.Dimensions.Select(x => $"{x.Name}={x.Value}")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            logger.LogWarning("Collection dispatch metric publish failed.");
        }
    }

    private Task PublishCounterAsync(string name, IReadOnlyCollection<Dimension> dimensions,
        CancellationToken cancellationToken)
        => PublishAsync([CountMetric(name, dimensions)], cancellationToken);

    private static MetricDatum CountMetric(string name, IReadOnlyCollection<Dimension> dimensions)
        => new()
        {
            MetricName = name,
            Dimensions = dimensions.ToList(),
            Timestamp = DateTime.UtcNow,
            Unit = StandardUnit.Count,
            Value = 1,
        };

    private MetricDatum Gauge(string name, double value, IReadOnlyCollection<Dimension>? dimensions = null,
        StandardUnit? unit = null)
        => new()
        {
            MetricName = name,
            Dimensions = dimensions?.ToList() ?? [],
            Timestamp = DateTime.UtcNow,
            Unit = unit ?? StandardUnit.Count,
            Value = value,
        };

    private void AddLaneDefinition(List<Dimension> dimensions, CollectionLane? lane, string? definitionId)
    {
        if (lane.HasValue && Enum.IsDefined(lane.Value)) dimensions.Add(Dimension("Lane", lane.Value.ToString()));
        if (definitionId is not null) dimensions.Add(Dimension("Definition", BoundedDefinition(definitionId)));
    }

    internal string BoundedDefinition(string definitionId)
        => _definitionLabels.Contains(definitionId) ? definitionId.ToLowerInvariant() : "OTHER";

    internal static bool IsSafeDefinitionLabel(string value)
        => value.Length is > 0 and <= 64
            && value.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z'
                or >= '0' and <= '9' or '-');

    private static Dimension Dimension(string name, string value) => new() { Name = name, Value = value };
}
