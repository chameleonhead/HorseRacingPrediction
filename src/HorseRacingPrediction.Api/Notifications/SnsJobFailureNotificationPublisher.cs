using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Notifications;

public sealed class SnsJobFailureNotificationPublisher : IJobFailureNotificationPublisher
{
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

        if (string.IsNullOrWhiteSpace(_options.AdminBaseUrl))
            throw new InvalidOperationException("JobFailureNotifications:AdminBaseUrl is not configured.");

        var message = BuildSmsMessage(_options.AdminBaseUrl, notification);

        return _sns.PublishAsync(new PublishRequest
        {
            TopicArn = _options.TopicArn,
            Message = message,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["AWS.SNS.SMS.SMSType"] = new()
                {
                    DataType = "String",
                    StringValue = "Transactional"
                }
            }
        }, cancellationToken);
    }

    internal static string BuildSmsMessage(string adminBaseUrl, PendingJobFailureNotification notification)
    {
        var jobUrl = $"{adminBaseUrl.TrimEnd('/')}/api/collection/tasks/{Uri.EscapeDataString(notification.JobId)}";
        return $"HRP {notification.Status} {notification.JobType}\n{jobUrl}";
    }
}
