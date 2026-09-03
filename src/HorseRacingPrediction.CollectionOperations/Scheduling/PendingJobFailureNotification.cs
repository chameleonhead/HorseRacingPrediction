namespace HorseRacingPrediction.Collector.Scheduling;

public sealed record PendingJobFailureNotification(
    string NotificationId,
    string JobId,
    string JobType,
    string DeduplicationKey,
    string Status,
    string? Error,
    int AttemptCount,
    DateTimeOffset FailedAt,
    int PublishAttemptCount);
