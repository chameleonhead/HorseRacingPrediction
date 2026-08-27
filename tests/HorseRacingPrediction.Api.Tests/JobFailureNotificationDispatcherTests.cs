using HorseRacingPrediction.Api.Notifications;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class JobFailureNotificationDispatcherTests
{
    [TestMethod]
    public async Task DispatchOnceAsync_PublishesAndCompletesNotificationOutbox()
    {
        var directory = Path.Combine(Path.GetTempPath(), "job-failure-notification-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProcessingStateStore(
                Options.Create(new AgentProcessingOptions { StateDirectory = directory }),
                NullLogger<ProcessingStateStore>.Instance);
            await store.ScheduleJobAsync("RaceCardCollection", "failed-task", "{}", DateTimeOffset.UtcNow);
            await store.FailJobAsync("RaceCardCollection", "failed-task", "playwright failed");
            var publisher = new RecordingPublisher();
            var dispatcher = new JobFailureNotificationDispatcher(
                store,
                publisher,
                Options.Create(new JobFailureNotificationOptions { Enabled = true, DispatchBatchSize = 10 }),
                NullLogger<JobFailureNotificationDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            Assert.AreEqual(1, publisher.Notifications.Count);
            Assert.AreEqual("failed-task", publisher.Notifications[0].DeduplicationKey);
            Assert.AreEqual(0, (await store.GetPendingJobFailureNotificationsAsync(DateTimeOffset.UtcNow.AddMinutes(1), 10)).Count);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DispatchOnceAsync_WhenPublishFails_LeavesNotificationForDelayedRetry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "job-failure-notification-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProcessingStateStore(
                Options.Create(new AgentProcessingOptions { StateDirectory = directory }),
                NullLogger<ProcessingStateStore>.Instance);
            await store.ScheduleJobAsync("RaceCardCollection", "failed-publish", "{}", DateTimeOffset.UtcNow);
            await store.FailJobAsync("RaceCardCollection", "failed-publish", "collection failed");
            var dispatcher = new JobFailureNotificationDispatcher(
                store,
                new FailingPublisher(),
                Options.Create(new JobFailureNotificationOptions { Enabled = true, DispatchBatchSize = 10 }),
                NullLogger<JobFailureNotificationDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            Assert.AreEqual(0, (await store.GetPendingJobFailureNotificationsAsync(DateTimeOffset.UtcNow, 10)).Count);
            var retry = await store.GetPendingJobFailureNotificationsAsync(DateTimeOffset.UtcNow.AddMinutes(2), 10);
            Assert.AreEqual(1, retry.Count);
            Assert.AreEqual(1, retry[0].PublishAttemptCount);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingPublisher : IJobFailureNotificationPublisher
    {
        public List<PendingJobFailureNotification> Notifications { get; } = [];

        public Task PublishAsync(PendingJobFailureNotification notification, CancellationToken cancellationToken)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingPublisher : IJobFailureNotificationPublisher
    {
        public Task PublishAsync(PendingJobFailureNotification notification, CancellationToken cancellationToken)
            => throw new InvalidOperationException("SNS unavailable");
    }
}
