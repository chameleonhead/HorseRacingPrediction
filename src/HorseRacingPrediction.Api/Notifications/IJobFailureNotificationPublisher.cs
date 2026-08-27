using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Api.Notifications;

public interface IJobFailureNotificationPublisher
{
    Task PublishAsync(PendingJobFailureNotification notification, CancellationToken cancellationToken);
}
