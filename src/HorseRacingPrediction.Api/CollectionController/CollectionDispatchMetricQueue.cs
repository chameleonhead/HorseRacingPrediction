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
public sealed class CollectionDispatchMetricQueue(
    ICollectionDispatchMetricPublisher publisher,
    IOptions<CollectionDispatchMetricQueueOptions> options,
    ILogger<CollectionDispatchMetricQueue> logger) : ICollectionDispatchMetricQueue, IHostedService, IDisposable
{
    private readonly CollectionDispatchMetricQueueOptions _options = Normalize(options.Value);
    private readonly Channel<WorkItem> _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(
        Math.Clamp(options.Value.Capacity, 1, 4096))
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private long _droppedItems;

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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.PublishTimeout);
        try
        {
            await publisher.PublishAsync(metrics.ToArray(), timeout.Token).WaitAsync(timeout.Token)
                .ConfigureAwait(false);
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
            logger.LogWarning("Collection dispatch metric publish timed out; background publisher stopped.");
            return false;
        }
        catch
        {
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
