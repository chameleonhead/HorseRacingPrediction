using System.Threading.Channels;
using Amazon.CloudWatch.Model;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.CollectionController;

public interface ICollectionDispatchMetricQueue
{
    bool TryEnqueueMetrics(IReadOnlyCollection<MetricDatum> metrics);
    bool TryEnqueueSnapshot(Func<CancellationToken, Task<IReadOnlyCollection<MetricDatum>>> snapshot);
}

public sealed class CollectionDispatchMetricQueueOptions
{
    public int Capacity { get; set; } = 128;
    public int MaximumBatchSize { get; set; } = 1000;
    public TimeSpan BatchWindow { get; set; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan SnapshotTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>Bounded, best-effort telemetry path. Producers only perform a nonblocking TryWrite.</summary>
public sealed class CollectionDispatchMetricQueue : ICollectionDispatchMetricQueue, IHostedService, IDisposable
{
    private readonly ICollectionDispatchMetricPublisher publisher;
    private readonly ILogger<CollectionDispatchMetricQueue> logger;
    private readonly CollectionRuntimeStatusRecorder? runtimeStatus;
    private readonly CollectionDispatchMetricQueueOptions _options;
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private long _droppedItems;

    public CollectionDispatchMetricQueue(ICollectionDispatchMetricPublisher publisher,
        IOptions<CollectionDispatchMetricQueueOptions> options, ILogger<CollectionDispatchMetricQueue> logger)
        : this(publisher, options, logger, null)
    {
    }

    internal CollectionDispatchMetricQueue(ICollectionDispatchMetricPublisher publisher,
        IOptions<CollectionDispatchMetricQueueOptions> options, ILogger<CollectionDispatchMetricQueue> logger,
        CollectionRuntimeStatusRecorder? runtimeStatus)
    {
        this.publisher = publisher;
        this.logger = logger;
        this.runtimeStatus = runtimeStatus;
        _options = Normalize(options.Value);
        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(_options.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    private readonly Channel<WorkItem> _channel;

    public bool TryEnqueueMetrics(IReadOnlyCollection<MetricDatum> metrics)
    {
        if (metrics.Count == 0) return true;
        try
        {
            var copied = metrics.Take(_options.MaximumBatchSize).ToArray();
            var accepted = copied.Length == metrics.Count && _channel.Writer.TryWrite(new(copied, null));
            if (!accepted) Interlocked.Increment(ref _droppedItems);
            return accepted;
        }
        catch
        {
            Interlocked.Increment(ref _droppedItems);
            return false;
        }
    }

    public bool TryEnqueueSnapshot(Func<CancellationToken, Task<IReadOnlyCollection<MetricDatum>>> snapshot)
    {
        try
        {
            var accepted = _channel.Writer.TryWrite(new(null, snapshot));
            if (!accepted) Interlocked.Increment(ref _droppedItems);
            return accepted;
        }
        catch
        {
            Interlocked.Increment(ref _droppedItems);
            return false;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _worker = RunAsync(_stop.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        if (_worker is null) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ShutdownDrainTimeout);
        try
        {
            await _worker.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _stop.Cancel();
            while (_channel.Reader.TryRead(out _)) { }
            logger.LogWarning("Collection dispatch telemetry shutdown drain timed out; queued telemetry dropped.");
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var pending = new List<MetricDatum>(_options.MaximumBatchSize);
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ReportDroppedItems();
            if (_options.BatchWindow > TimeSpan.Zero)
                await Task.Delay(_options.BatchWindow, cancellationToken).ConfigureAwait(false);
            while (_channel.Reader.TryRead(out var work))
            {
                IReadOnlyCollection<MetricDatum> metrics;
                try
                {
                    if (work.Snapshot is null) metrics = work.Metrics ?? [];
                    else
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeout.CancelAfter(_options.SnapshotTimeout);
                        metrics = await work.Snapshot(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
                catch
                {
                    logger.LogWarning("Collection dispatch telemetry snapshot query failed.");
                    var failedToken = runtimeStatus?.BeginCycle(CollectionRuntimeAction.MetricDelivery);
                    if (failedToken is { } cycleToken) runtimeStatus!.FailCycle(cycleToken);
                    continue;
                }

                foreach (var metric in metrics)
                {
                    pending.Add(metric);
                    if (pending.Count == _options.MaximumBatchSize)
                    {
                        if (!await PublishBatchAsync(pending, cancellationToken).ConfigureAwait(false))
                        {
                            pending.Clear();
                            while (_channel.Reader.TryRead(out _)) { }
                            return;
                        }
                        pending.Clear();
                    }
                }
            }
            if (pending.Count > 0)
            {
                if (!await PublishBatchAsync(pending, cancellationToken).ConfigureAwait(false))
                {
                    while (_channel.Reader.TryRead(out _)) { }
                    return;
                }
                pending.Clear();
            }
        }
        if (pending.Count > 0) _ = await PublishBatchAsync(pending, cancellationToken).ConfigureAwait(false);
    }

    private void ReportDroppedItems()
    {
        var dropped = Interlocked.Exchange(ref _droppedItems, 0);
        if (dropped > 0) logger.LogWarning("Collection dispatch telemetry work dropped {DroppedCount}.", dropped);
    }

    private async Task<bool> PublishBatchAsync(List<MetricDatum> metrics, CancellationToken cancellationToken)
    {
        var token = runtimeStatus?.BeginCycle(CollectionRuntimeAction.MetricDelivery);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.PublishTimeout);
        try
        {
            await publisher.PublishAsync(metrics.ToArray(), timeout.Token).WaitAsync(timeout.Token)
                .ConfigureAwait(false);
            if (token is { } progressToken) runtimeStatus!.RecordProgress(progressToken);
            if (token is { } completedToken)
                runtimeStatus!.CompleteCycle(completedToken, null,
                    new(Inspected: metrics.Count, Sent: metrics.Count, Completed: metrics.Count));
            foreach (var metric in metrics)
            {
                var dimensions = string.Join(',', metric.Dimensions.OrderBy(x => x.Name, StringComparer.Ordinal)
                    .Select(x => $"{x.Name}={x.Value}"));
                logger.LogInformation("Collection dispatch metric published {MetricName} {MetricValue} {Dimensions}",
                    metric.MetricName, metric.Value, dimensions);
            }
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (token is { } failedToken) runtimeStatus!.FailCycle(failedToken);
            logger.LogWarning("Collection dispatch metric publish timed out; background publisher stopped.");
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (token is { } cancelledToken) runtimeStatus!.CancelCycle(cancelledToken);
            throw;
        }
        catch
        {
            if (token is { } failedToken) runtimeStatus!.FailCycle(failedToken,
                new(Inspected: metrics.Count));
            logger.LogWarning("Collection dispatch metric publish failed.");
            return true;
        }
    }

    private static CollectionDispatchMetricQueueOptions Normalize(CollectionDispatchMetricQueueOptions value)
        => new()
        {
            Capacity = Math.Clamp(value.Capacity, 1, 4096),
            MaximumBatchSize = Math.Clamp(value.MaximumBatchSize, 1, 1000),
            BatchWindow = TimeSpan.FromMilliseconds(Math.Clamp(value.BatchWindow.TotalMilliseconds, 0, 1000)),
            PublishTimeout = TimeSpan.FromMilliseconds(Math.Clamp(value.PublishTimeout.TotalMilliseconds, 1, 30000)),
            SnapshotTimeout = TimeSpan.FromMilliseconds(Math.Clamp(value.SnapshotTimeout.TotalMilliseconds, 1, 30000)),
            ShutdownDrainTimeout = TimeSpan.FromMilliseconds(Math.Clamp(value.ShutdownDrainTimeout.TotalMilliseconds, 1, 30000)),
        };

    private sealed record WorkItem(IReadOnlyCollection<MetricDatum>? Metrics,
        Func<CancellationToken, Task<IReadOnlyCollection<MetricDatum>>>? Snapshot);
}
