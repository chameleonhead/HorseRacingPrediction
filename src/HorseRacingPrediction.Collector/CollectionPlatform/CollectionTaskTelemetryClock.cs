namespace HorseRacingPrediction.Collector.CollectionPlatform;

internal interface ICollectionTaskTelemetryClock
{
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp);
}

internal sealed class SystemCollectionTaskTelemetryClock : ICollectionTaskTelemetryClock
{
    public static SystemCollectionTaskTelemetryClock Instance { get; } = new();

    private SystemCollectionTaskTelemetryClock()
    {
    }

    public long GetTimestamp() => TimeProvider.System.GetTimestamp();

    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
        => TimeProvider.System.GetElapsedTime(startingTimestamp, endingTimestamp);
}
