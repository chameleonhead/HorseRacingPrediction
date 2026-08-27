namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class JobFailureNotificationEntity
{
    public string NotificationId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string DeduplicationKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset FailedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public int PublishAttemptCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? LastPublishError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
