namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record AgentAcquisitionHistoryReadModel(
    long Sequence,
    string AcquisitionKey,
    string? ProviderType,
    RaceDataCollectionState Status,
    RaceDataCollectionErrorCode? ErrorCode,
    string? ErrorReason,
    string? OriginJobId,
    string? SourceUrl,
    DateTimeOffset OccurredAt);
