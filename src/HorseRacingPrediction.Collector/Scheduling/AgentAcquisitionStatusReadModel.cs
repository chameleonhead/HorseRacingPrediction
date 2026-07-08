namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record AgentAcquisitionStatusReadModel(
    AgentAcquisitionSubjectType SubjectType,
    AgentAcquisitionOperationType OperationType,
    string? ProviderType,
    string? SubjectId,
    string SubjectName,
    string? RelatedRaceId,
    string? SourceUrl,
    RaceDataCollectionState Status,
    RaceDataCollectionErrorCode? ErrorCode,
    string? ErrorReason,
    DateTimeOffset UpdatedAt);