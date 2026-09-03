namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class AgentAcquisitionHistoryEntity
{
    public long Sequence { get; set; }
    public string AcquisitionKey { get; set; } = string.Empty;
    public string? ProviderType { get; set; }
    public RaceDataCollectionState Status { get; set; }
    public RaceDataCollectionErrorCode? ErrorCode { get; set; }
    public string? ErrorReason { get; set; }
    public string? OriginJobId { get; set; }
    public string? SourceUrl { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
