namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record CollectionPipelineState(bool IsPaused, string? Reason = null, string? JobId = null, DateTimeOffset? StoppedAt = null);
public enum CollectionLeaseControl { Continue, Hold, LeaseLost }

public static class CollectionControlKeys
{
    public const string MarkerType = "CollectionMaintenance";
    public const string MarkerKey = "Paused";
    public const string ReasonType = "CollectionStopReason";
}
