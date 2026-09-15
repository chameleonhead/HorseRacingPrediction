namespace HorseRacingPrediction.Collector.CollectionPlatform;

internal static class CollectionRuntimeTimingContext
{
    private static readonly AsyncLocal<TimingAccumulator?> CurrentTask = new();
    private static readonly AsyncLocal<TimingAccumulator?> CurrentSession = new();

    public static TimingScope BeginTask() => Begin(CurrentTask);
    public static TimingScope BeginSession() => Begin(CurrentSession);

    public static void RecordInternalApi(TimeSpan elapsed)
    {
        CurrentTask.Value?.Record(elapsed);
        CurrentSession.Value?.Record(elapsed);
    }

    private static TimingScope Begin(AsyncLocal<TimingAccumulator?> target)
    {
        var previous = target.Value;
        var accumulator = new TimingAccumulator();
        target.Value = accumulator;
        return new TimingScope(target, previous, accumulator);
    }

    internal sealed class TimingScope(AsyncLocal<TimingAccumulator?> target,
        TimingAccumulator? previous, TimingAccumulator accumulator) : IDisposable
    {
        private bool _disposed;
        public TimingAccumulator Accumulator { get; } = accumulator;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            target.Value = previous;
        }
    }

    internal sealed class TimingAccumulator
    {
        private readonly object _sync = new();
        private TimeSpan _apiElapsed;
        private int _apiCallCount;

        public void Record(TimeSpan elapsed)
        {
            lock (_sync)
            {
                _apiElapsed += elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
                _apiCallCount++;
            }
        }

        public TimingSnapshot Snapshot()
        {
            lock (_sync) return new(_apiElapsed, _apiCallCount);
        }
    }

    internal readonly record struct TimingSnapshot(TimeSpan ApiElapsed, int ApiCallCount);
}
