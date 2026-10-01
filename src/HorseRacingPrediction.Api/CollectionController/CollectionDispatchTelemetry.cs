using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
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
        CollectionLane? lane = null, string? definitionId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RecordReservationReleaseAsync(CollectionReservationReleaseOutcome outcome,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RecordLeaseReclaimedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RecordTerminalCompletionAsync(CollectionLane lane, string definitionId, CollectionTaskStatus status,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RecordTerminalCompletionLookupAsync(Func<CancellationToken, Task<CollectionDispatchTaskTelemetryState?>> lookup,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task QueueSnapshotAsync(Func<CancellationToken, Task<CollectionDispatchTelemetrySnapshot>> query,
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
    ICollectionDispatchMetricQueue queue,
    IOptions<CollectionQueueOptions> queueOptions) : ICollectionDispatchTelemetry
{
    public const string Namespace = "HorseRacingPrediction/CollectionDispatch";
    private long _lastSnapshotUtcTicks;

    private static readonly string[] DefinitionAllowlist = ["race-discovery", "race-detail", "race-odds"];
    private readonly HashSet<string> _definitionLabels = (queueOptions.Value.TelemetryDefinitionLabels.Count == 0
            ? (IEnumerable<string>)DefinitionAllowlist
            : queueOptions.Value.TelemetryDefinitionLabels)
        .Where(IsSafeDefinitionLabel).Take(16).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private readonly bool _enabled = queueOptions.Value.Enabled
        && string.Equals(queueOptions.Value.Provider, "Sqs", StringComparison.OrdinalIgnoreCase);

    public Task RecordDispatchCycleAsync(CollectionDispatchCycleOutcome outcome, CollectionLane? lane = null,
        string? definitionId = null, CancellationToken cancellationToken = default)
    {
        var outcomeValue = Enum.IsDefined(outcome) ? outcome.ToString() : "Unknown";
        var aggregate = CountMetric("dispatch_cycle_total", [Dimension("Outcome", outcomeValue)]);
        var metrics = new List<MetricDatum> { aggregate };
        var detail = LaneDefinitionDimensions(lane, definitionId);
        if (detail.Count == 2)
            metrics.Add(CountMetric("dispatch_cycle_by_lane_definition_total",
                [Dimension("Outcome", outcomeValue), .. detail]));
        if (outcome == CollectionDispatchCycleOutcome.WakeSent)
        {
            metrics.Add(CountMetric("wake_sent_total", []));
            if (detail.Count == 2) metrics.Add(CountMetric("wake_sent_by_lane_definition_total", detail));
        }
        if (outcome is CollectionDispatchCycleOutcome.WakeSendDefiniteFailure
            or CollectionDispatchCycleOutcome.WakeSendAmbiguousFailure)
            metrics.Add(CountMetric("queue_send_failure_total", []));
        Enqueue(metrics);
        return Task.CompletedTask;
    }

    public Task RecordAcquireAsync(CollectionExecutionAcquireStatus status, CollectionExecutionNoWorkReason? reason,
        CollectionLane? lane = null, string? definitionId = null, CancellationToken cancellationToken = default)
    {
        var boundedReason = status == CollectionExecutionAcquireStatus.Acquired
            ? "Acquired" : reason is { } value && Enum.IsDefined(value) ? value.ToString() : "Unknown";
        var boundedStatus = Enum.IsDefined(status) ? status.ToString() : "Unknown";
        var metrics = new List<MetricDatum>
        {
            CountMetric("acquire_total", [Dimension("Status", boundedStatus), Dimension("Reason", boundedReason)]),
        };
        if (status == CollectionExecutionAcquireStatus.NoWork) metrics.Add(CountMetric("acquire_nowork_total", []));
        if (status == CollectionExecutionAcquireStatus.Acquired)
        {
            metrics.Add(CountMetric("acquire_success_total", []));
            var detail = LaneDefinitionDimensions(lane, definitionId);
            if (detail.Count == 2) metrics.Add(CountMetric("acquire_success_by_lane_definition_total", detail));
        }
        Enqueue(metrics);
        return Task.CompletedTask;
    }

    public Task RecordReservationReleaseAsync(CollectionReservationReleaseOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        var boundedOutcome = Enum.IsDefined(outcome) ? outcome.ToString() : "Unknown";
        var metrics = new List<MetricDatum>
        {
            CountMetric("reservation_release_total", []),
            CountMetric("reservation_release_by_outcome_total", [Dimension("Outcome", boundedOutcome)]),
        };
        if (outcome != CollectionReservationReleaseOutcome.Released)
            metrics.Add(CountMetric("reservation_release_failure_total", []));
        Enqueue(metrics);
        return Task.CompletedTask;
    }

    public Task RecordLeaseReclaimedAsync(CancellationToken cancellationToken = default)
    {
        Enqueue([CountMetric("execution_lease_reclaimed_total", [Dimension("Reason", "Expired")])]);
        return Task.CompletedTask;
    }

    public Task RecordTerminalCompletionAsync(CollectionLane lane, string definitionId, CollectionTaskStatus status,
        CancellationToken cancellationToken = default)
    {
        if (status is CollectionTaskStatus.Succeeded or CollectionTaskStatus.Failed
            or CollectionTaskStatus.Cancelled or CollectionTaskStatus.DeadLetter)
        {
            Enqueue([CountMetric("terminal_task_completion_total", []),
                CountMetric("terminal_task_completion_by_lane_definition_total",
                    LaneDefinitionDimensions(lane, definitionId))]);
        }
        return Task.CompletedTask;
    }

    public Task RecordTerminalCompletionLookupAsync(
        Func<CancellationToken, Task<CollectionDispatchTaskTelemetryState?>> lookup,
        CancellationToken cancellationToken = default)
    {
        if (_enabled) SafeEnqueueSnapshot(async token =>
        {
            var state = await lookup(token).ConfigureAwait(false);
            return state is null ? [] : TerminalMetrics(state);
        });
        return Task.CompletedTask;
    }

    public Task QueueSnapshotAsync(Func<CancellationToken, Task<CollectionDispatchTelemetrySnapshot>> query,
        CancellationToken cancellationToken = default)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var previous = Interlocked.Read(ref _lastSnapshotUtcTicks);
        if (previous != 0 && nowTicks - previous < TimeSpan.TicksPerMinute) return Task.CompletedTask;
        if (Interlocked.CompareExchange(ref _lastSnapshotUtcTicks, nowTicks, previous) != previous)
            return Task.CompletedTask;
        if (_enabled) SafeEnqueueSnapshot(async token => SnapshotMetrics(await query(token).ConfigureAwait(false)));
        return Task.CompletedTask;
    }

    private IReadOnlyCollection<MetricDatum> TerminalMetrics(CollectionDispatchTaskTelemetryState state)
    {
        if (state.Status is not (CollectionTaskStatus.Succeeded or CollectionTaskStatus.Failed
            or CollectionTaskStatus.Cancelled or CollectionTaskStatus.DeadLetter)) return [];
        return [CountMetric("terminal_task_completion_total", []),
            CountMetric("terminal_task_completion_by_lane_definition_total",
                LaneDefinitionDimensions(state.Lane, state.DefinitionId))];
    }

    private IReadOnlyCollection<MetricDatum> SnapshotMetrics(CollectionDispatchTelemetrySnapshot snapshot)
    {
        var metrics = new List<MetricDatum>
        {
            Gauge("ready_missing_current_outbox", snapshot.ReadyMissingCurrentOutbox),
            Gauge("outbox_cardinality_anomaly_tasks", snapshot.CardinalityAnomalyTasks),
            Gauge("active_eligible_reservations", snapshot.ActiveEligibleReservations),
            Gauge("expired_eligible_reservations", snapshot.ExpiredEligibleReservations),
            Gauge("in_flight_execution_leases", snapshot.InFlightExecutionLeases),
            Gauge("max_in_flight_envelopes", Math.Max(1, snapshot.MaxInFlightEnvelopes)),
            Gauge("eligible_in_flight_count", snapshot.EligibleInFlightCount),
        };
        foreach (var lane in snapshot.Lanes)
        {
            var dimensions = LaneDefinitionDimensions(lane.Lane, lane.DefinitionId);
            metrics.Add(Gauge("eligible_ready_rows", lane.EligibleReadyRows, dimensions));
            metrics.Add(Gauge("oldest_eligible_age_seconds", lane.OldestEligibleAgeSeconds, dimensions, StandardUnit.Seconds));
            metrics.Add(Gauge("active_eligible_reservations_by_lane", lane.ActiveEligibleReservations, dimensions));
            metrics.Add(Gauge("expired_eligible_reservations_by_lane", lane.ExpiredEligibleReservations, dimensions));
        }
        return metrics;
    }

    private void Enqueue(IReadOnlyCollection<MetricDatum> metrics)
    {
        if (!_enabled) return;
        try { _ = queue.TryEnqueueMetrics(metrics); }
        catch { }
    }

    private void SafeEnqueueSnapshot(Func<CancellationToken, Task<IReadOnlyCollection<MetricDatum>>> snapshot)
    {
        try { _ = queue.TryEnqueueSnapshot(snapshot); }
        catch { }
    }

    private List<Dimension> LaneDefinitionDimensions(CollectionLane? lane, string? definitionId)
    {
        if (lane is null || !Enum.IsDefined(lane.Value) || definitionId is null) return [];
        return [Dimension("Lane", lane.Value.ToString()), Dimension("Definition", BoundedDefinition(definitionId))];
    }

    private MetricDatum CountMetric(string name, IReadOnlyCollection<Dimension> dimensions) => new()
    {
        MetricName = name,
        Dimensions = dimensions.ToList(),
        Timestamp = DateTime.UtcNow,
        Unit = StandardUnit.Count,
        Value = 1,
    };

    private MetricDatum Gauge(string name, double value, IReadOnlyCollection<Dimension>? dimensions = null,
        StandardUnit? unit = null) => new()
        {
            MetricName = name,
            Dimensions = dimensions?.ToList() ?? [],
            Timestamp = DateTime.UtcNow,
            Unit = unit ?? StandardUnit.Count,
            Value = value,
        };

    internal string BoundedDefinition(string definitionId)
        => _definitionLabels.Contains(definitionId) ? definitionId.ToLowerInvariant() : "OTHER";
    internal static bool IsSafeDefinitionLabel(string value)
        => value.Length is > 0 and <= 64 && value.All(character => character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-');
    private static Dimension Dimension(string name, string value) => new() { Name = name, Value = value };
}
