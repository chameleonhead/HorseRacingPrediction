using System.Diagnostics;

namespace HorseRacingPrediction.Scraping.Browser.Diagnostics;

/// <summary>
/// Small, dependency-free measurement seam for repeatable browser pipeline diagnostics.
/// Allocation values use the process-wide managed allocation counter so they remain usable
/// across asynchronous continuations; callers must treat them as approximate when other work
/// runs concurrently.
/// </summary>
public static class PerformanceProbe
{
    public static PerformanceSample Measure(string stage, Action operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(operation);

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        var startedAt = Stopwatch.GetTimestamp();
        operation();
        return Complete(stage, startedAt, allocatedBefore);
    }

    public static async Task<PerformanceSample> MeasureAsync(string stage, Func<Task> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(operation);

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        var startedAt = Stopwatch.GetTimestamp();
        await operation().ConfigureAwait(false);
        return Complete(stage, startedAt, allocatedBefore);
    }

    public static PerformanceSummary Summarize(string stage, IEnumerable<PerformanceSample> samples)
    {
        var values = samples.Where(sample => sample.Stage == stage).ToArray();
        if (values.Length == 0) throw new ArgumentException($"No samples exist for stage '{stage}'.", nameof(samples));

        var elapsed = values.Select(sample => sample.Elapsed.TotalMilliseconds).Order().ToArray();
        var allocated = values.Select(sample => sample.ApproximateAllocatedBytes).Order().ToArray();
        return new(stage, values.Length, elapsed.Average(), Percentile(elapsed, 0.50), Percentile(elapsed, 0.95),
            (long)allocated.Average(), Percentile(allocated, 0.95));
    }

    private static PerformanceSample Complete(string stage, long startedAt, long allocatedBefore) =>
        new(stage, Stopwatch.GetElapsedTime(startedAt),
            Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore));

    private static double Percentile(IReadOnlyList<double> sorted, double percentile) =>
        sorted[(int)Math.Ceiling(percentile * sorted.Count) - 1];

    private static long Percentile(IReadOnlyList<long> sorted, double percentile) =>
        sorted[(int)Math.Ceiling(percentile * sorted.Count) - 1];
}

public sealed record PerformanceSample(string Stage, TimeSpan Elapsed, long ApproximateAllocatedBytes);

public sealed record PerformanceSummary(string Stage, int Iterations, double MeanElapsedMilliseconds,
    double P50ElapsedMilliseconds, double P95ElapsedMilliseconds, long MeanApproximateAllocatedBytes,
    long P95ApproximateAllocatedBytes);
