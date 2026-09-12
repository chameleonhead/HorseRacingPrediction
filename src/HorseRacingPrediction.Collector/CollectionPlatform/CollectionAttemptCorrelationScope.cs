using System.Threading;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public static class CollectionAttemptCorrelationScope
{
    private static readonly AsyncLocal<CollectionAttemptCorrelation?> CurrentValue = new();

    public static CollectionAttemptCorrelation? Current => CurrentValue.Value;

    public static IDisposable Push(CollectionAttemptCorrelation correlation)
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = correlation;
        return new PopScope(previous);
    }

    private sealed class PopScope(CollectionAttemptCorrelation? previous) : IDisposable
    {
        public void Dispose() => CurrentValue.Value = previous;
    }
}
