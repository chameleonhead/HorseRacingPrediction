using System.Globalization;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Notifications;

public sealed class SnsJobFailureNotificationPublisher : IJobFailureNotificationPublisher
{
    private const int MaxErrorLength = 4000;
    private static readonly TimeZoneInfo Jst = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");
    private readonly IAmazonSimpleNotificationService _sns;
    private readonly JobFailureNotificationOptions _options;

    public SnsJobFailureNotificationPublisher(
        IAmazonSimpleNotificationService sns,
        IOptions<JobFailureNotificationOptions> options)
    {
        _sns = sns;
        _options = options.Value;
    }

    public Task PublishAsync(PendingJobFailureNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.TopicArn))
            throw new InvalidOperationException("JobFailureNotifications:TopicArn is not configured.");

        var failedAtJst = TimeZoneInfo.ConvertTime(notification.FailedAt, Jst);
        var error = string.IsNullOrWhiteSpace(notification.Error)
            ? "No error message was recorded."
            : notification.Error.Length <= MaxErrorLength
                ? notification.Error
                : notification.Error[..MaxErrorLength] + "...";
        var adminUrl = string.IsNullOrWhiteSpace(_options.AdminBaseUrl)
            ? null
            : $"{_options.AdminBaseUrl.TrimEnd('/')}/collection-tasks";
        var message = string.Join(Environment.NewLine,
        [
            "A collection job failed and automatic execution has stopped.",
            string.Empty,
            $"Status: {notification.Status}",
            $"Job Type: {notification.JobType}",
            $"Job Key: {notification.DeduplicationKey}",
            $"Job ID: {notification.JobId}",
            $"Attempt Count: {notification.AttemptCount.ToString(CultureInfo.InvariantCulture)}",
            $"Failed At: {failedAtJst:yyyy-MM-dd HH:mm:ss} JST",
            $"Error: {error}",
            string.Empty,
            "Review the failure and manually requeue the job only when it is safe to continue; otherwise leave it Failed.",
            adminUrl is null ? "Admin URL: not configured" : $"Admin URL: {adminUrl}"
        ]);

        return _sns.PublishAsync(new PublishRequest
        {
            TopicArn = _options.TopicArn,
            Subject = $"[Horse Racing][{notification.Status}] {notification.JobType}",
            Message = message
        }, cancellationToken);
    }
}
