namespace HorseRacingPrediction.Contracts.Collection;

public sealed record CollectionFailureNotificationDto(Guid NotificationId, Guid TaskId,
    CollectionResourceKeyDto Resource, CollectionDefinitionIdDto Definition, CollectionTaskStatus Status,
    string? ErrorCode, string? ErrorMessage, int AttemptCount, DateTimeOffset FailedAt,
    CollectionFailureResolutionStatus ResolutionStatus = CollectionFailureResolutionStatus.Open,
    Guid? RecoveryTaskId = null, DateTimeOffset? RecoveryStartedAt = null, DateTimeOffset? ResolvedAt = null,
    string? SelectedUrl = null, CollectionResourceKeyDto? CanonicalResource = null,
    string? Selector = null, DateTimeOffset? SelectedAt = null);
