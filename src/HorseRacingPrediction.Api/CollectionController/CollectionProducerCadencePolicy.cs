namespace HorseRacingPrediction.Api.CollectionController;

internal static class CollectionProducerCadencePolicy
{
    internal static WatchdogCadence GetWatchdogCadence(
        bool backgroundSchedulersEnabled,
        bool watchdogEnabled,
        int configuredIntervalMinutes)
    {
        if (backgroundSchedulersEnabled) return new(true, 1);
        return new(watchdogEnabled, Math.Max(1, configuredIntervalMinutes));
    }

    internal static bool IsClockJump(
        DateTimeOffset previousWallClock,
        DateTimeOffset currentWallClock,
        TimeSpan monotonicElapsed,
        TimeSpan tolerance)
    {
        var wallElapsed = currentWallClock - previousWallClock;
        return (wallElapsed - monotonicElapsed).Duration() > tolerance;
    }

    internal static DateTimeOffset NextDueAfterCompletion(DateTimeOffset completedAt, TimeSpan cadence) =>
        completedAt + cadence;
}

internal readonly record struct WatchdogCadence(bool Enabled, int IntervalMinutes);
