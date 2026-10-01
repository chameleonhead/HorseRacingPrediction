namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionFailureTargetDto(Guid NotificationId, Guid TaskId, CollectionResourceKeyDto Resource,
    CollectionDefinitionIdDto Definition, CollectionTaskStatus Status, string? ErrorCode, string? ErrorMessage,
    int AttemptCount, DateTimeOffset FailedAt, string? RequestedUrl, string? FinalUrl, int? HttpStatusCode,
    string? PageIdentification, Guid? ExecutionBatchId, string? LambdaRequestId);
