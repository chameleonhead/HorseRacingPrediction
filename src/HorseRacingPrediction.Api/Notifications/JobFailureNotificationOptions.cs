namespace HorseRacingPrediction.Api.Notifications;

public sealed class JobFailureNotificationOptions
{
    public const string SectionName = "JobFailureNotifications";
    public bool Enabled { get; set; }
    public string TopicArn { get; set; } = string.Empty;
    public string AdminBaseUrl { get; set; } = string.Empty;
    public int DispatchIntervalSeconds { get; set; } = 5;
    public int DispatchBatchSize { get; set; } = 10;
}
