using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Notifications;

public sealed class JobFailureNotificationDispatcher : BackgroundService
{
    private readonly ProcessingStateStore _store;
    private readonly IJobFailureNotificationPublisher _publisher;
    private readonly JobFailureNotificationOptions _options;
    private readonly ILogger<JobFailureNotificationDispatcher> _logger;

    public JobFailureNotificationDispatcher(
        ProcessingStateStore store,
        IJobFailureNotificationPublisher publisher,
        IOptions<JobFailureNotificationOptions> options,
        ILogger<JobFailureNotificationDispatcher> logger)
    {
        _store = store;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job failure notification dispatch cycle failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.DispatchIntervalSeconds)), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        var notifications = await _store.GetPendingJobFailureNotificationsAsync(
            DateTimeOffset.UtcNow,
            Math.Max(1, _options.DispatchBatchSize),
            cancellationToken).ConfigureAwait(false);

        foreach (var notification in notifications)
        {
            try
            {
                await _publisher.PublishAsync(notification, cancellationToken).ConfigureAwait(false);
                await _store.MarkJobFailureNotificationPublishedAsync(
                    notification.NotificationId,
                    DateTimeOffset.UtcNow,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Job failure notification publish failed. NotificationId={NotificationId}", notification.NotificationId);
                await _store.MarkJobFailureNotificationPublishFailedAsync(
                    notification.NotificationId,
                    DateTimeOffset.UtcNow,
                    ex.Message,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
