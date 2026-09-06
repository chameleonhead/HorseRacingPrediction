namespace HorseRacingPrediction.Api.Notifications;

/// <summary>
/// 収集ジョブ全体の停止など、個別ジョブ単位のFailure通知（<see cref="IJobFailureNotificationPublisher"/>、
/// <see cref="Notifications.JobFailureNotificationOptions.Enabled"/> で有効/無効を切り替え可能）とは別に、
/// 常時（設定でのON/OFFに関わらず）SNSへ送信するための、システム全体の重大アラート用publisher。
/// </summary>
public interface ICollectionPipelineAlertPublisher
{
    Task PublishCollectionStoppedAsync(string reason, int dlqFailureCount, CancellationToken cancellationToken);
}
