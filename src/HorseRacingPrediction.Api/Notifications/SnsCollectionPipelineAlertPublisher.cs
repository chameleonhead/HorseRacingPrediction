using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Notifications;

/// <summary>
/// <see cref="JobFailureNotificationOptions.Enabled"/> のON/OFFに関わらず、常にSNSへ送信する。
/// トピックはSNSサブスクリプションの管理側（運用者）が別途作成する前提のため、こちら側の設定で
/// 送信有無を制御しない。TopicArn未設定の場合のみ、送信できない旨をログに残してスキップする。
/// </summary>
public sealed class SnsCollectionPipelineAlertPublisher : ICollectionPipelineAlertPublisher
{
    private readonly IAmazonSimpleNotificationService _sns;
    private readonly JobFailureNotificationOptions _options;
    private readonly ILogger<SnsCollectionPipelineAlertPublisher> _logger;

    public SnsCollectionPipelineAlertPublisher(
        IAmazonSimpleNotificationService sns,
        IOptions<JobFailureNotificationOptions> options,
        ILogger<SnsCollectionPipelineAlertPublisher> logger)
    {
        _sns = sns;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishCollectionStoppedAsync(string reason, int dlqFailureCount, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.TopicArn))
        {
            _logger.LogWarning(
                "収集ジョブ停止アラートを送信できません（JobFailureNotifications:TopicArn が未設定）。Reason={Reason}",
                reason);
            return;
        }

        await _sns.PublishAsync(new PublishRequest
        {
            TopicArn = _options.TopicArn,
            Message = $"HRP ALERT: Collection pipeline stopped.\n{reason}\nDlqFailureCount={dlqFailureCount}",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["AWS.SNS.SMS.SMSType"] = new()
                {
                    DataType = "String",
                    StringValue = "Transactional"
                }
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
