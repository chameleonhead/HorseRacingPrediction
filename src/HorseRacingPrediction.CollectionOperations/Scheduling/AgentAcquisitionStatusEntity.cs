namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class AgentAcquisitionStatusEntity
{
    public string AcquisitionKey { get; set; } = string.Empty;
    public AgentAcquisitionSubjectType SubjectType { get; set; }
    public AgentAcquisitionOperationType OperationType { get; set; }
    public string? ProviderType { get; set; }
    public string? SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string? RelatedRaceId { get; set; }
    public string? OriginJobId { get; set; }
    public string? SourceUrl { get; set; }
    public RaceDataCollectionState Status { get; set; }
    public RaceDataCollectionErrorCode? ErrorCode { get; set; }
    public string? ErrorReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
