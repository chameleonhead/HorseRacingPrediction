namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record AgentAcquisitionStatusReadModel(
    string AcquisitionKey,
    AgentAcquisitionSubjectType SubjectType,
    AgentAcquisitionOperationType OperationType,
    string? ProviderType,
    string? SubjectId,
    string SubjectName,
    string? RelatedRaceId,
    string? OriginJobId,
    string? SourceUrl,
    RaceDataCollectionState Status,
    RaceDataCollectionErrorCode? ErrorCode,
    string? ErrorReason,
    DateTimeOffset UpdatedAt,
    DateTimeOffset CreatedAt);
