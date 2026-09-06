using System.Text.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Notifications;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionDeadLetterQueueReconcilerTests
{
    [TestMethod]
    public async Task RunOnceAsync_MarksJobFailedAndDeletesMessage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-dlq-reconciler-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var processingOptions = Options.Create(new AgentProcessingOptions { StateDirectory = directory });
            var store = new ProcessingStateStore(processingOptions, NullLogger<ProcessingStateStore>.Instance);
            var now = DateTimeOffset.UtcNow;
            await store.ScheduleJobAsync("RaceCardCollection", "task-1", "{}", now);

            var notification = new CollectionTaskNotification("RaceCardCollection:task-1", "RaceCardCollection", "task-1");
            var queue = new FakeQueue([new DeadLetterQueueMessage("receipt-1", JsonSerializer.Serialize(notification))]);
            var maintenance = new CollectionMaintenanceState();
            var alertPublisher = new FakeAlertPublisher();
            var reconciler = new CollectionDeadLetterQueueReconciler(
                store,
                queue,
                maintenance,
                alertPublisher,
                Options.Create(new CollectionDeadLetterQueueReconcilerOptions { ConsecutiveFailureThreshold = 3 }),
                NullLogger<CollectionDeadLetterQueueReconciler>.Instance);

            await reconciler.RunOnceAsync(CancellationToken.None);

            var detail = await store.GetJobDetailAsync("RaceCardCollection:task-1");
            Assert.AreEqual(AgentJobStatus.Failed, detail!.Status);
            CollectionAssert.AreEqual(new[] { "receipt-1" }, queue.DeletedReceiptHandles.ToArray());
            Assert.AreEqual(1, maintenance.DlqFailureCount);
            Assert.IsFalse(maintenance.IsActive);
            Assert.AreEqual(0, alertPublisher.Calls.Count);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task RunOnceAsync_StopsCollectionAndAlertsOnceThresholdReached()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-dlq-reconciler-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var processingOptions = Options.Create(new AgentProcessingOptions { StateDirectory = directory });
            var store = new ProcessingStateStore(processingOptions, NullLogger<ProcessingStateStore>.Instance);
            var now = DateTimeOffset.UtcNow;
            await store.ScheduleJobAsync("RaceCardCollection", "task-a", "{}", now);
            await store.ScheduleJobAsync("RaceCardCollection", "task-b", "{}", now);

            var messages = new[] { "task-a", "task-b" }
                .Select(key => new DeadLetterQueueMessage(
                    $"receipt-{key}",
                    JsonSerializer.Serialize(new CollectionTaskNotification($"RaceCardCollection:{key}", "RaceCardCollection", key))))
                .ToList();
            var queue = new FakeQueue(messages);
            var maintenance = new CollectionMaintenanceState();
            var alertPublisher = new FakeAlertPublisher();
            var reconciler = new CollectionDeadLetterQueueReconciler(
                store,
                queue,
                maintenance,
                alertPublisher,
                Options.Create(new CollectionDeadLetterQueueReconcilerOptions { ConsecutiveFailureThreshold = 2 }),
                NullLogger<CollectionDeadLetterQueueReconciler>.Instance);

            await reconciler.RunOnceAsync(CancellationToken.None);

            Assert.AreEqual(2, maintenance.DlqFailureCount);
            Assert.IsTrue(maintenance.IsActive);
            Assert.IsTrue(queue.Purged);
            Assert.AreEqual(1, alertPublisher.Calls.Count);
            Assert.AreEqual(2, alertPublisher.Calls[0].DlqFailureCount);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeQueue(IReadOnlyList<DeadLetterQueueMessage> messages) : ICollectionTaskQueue
    {
        public List<string> DeletedReceiptHandles { get; } = [];
        public bool Purged { get; private set; }

        public Task SendAsync(CollectionTaskNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task PurgeAsync(CancellationToken cancellationToken)
        {
            Purged = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DeadLetterQueueMessage>> ReceiveDeadLetterMessagesAsync(int maxMessages, CancellationToken cancellationToken)
            => Task.FromResult(messages);

        public Task DeleteDeadLetterMessageAsync(string receiptHandle, CancellationToken cancellationToken)
        {
            DeletedReceiptHandles.Add(receiptHandle);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAlertPublisher : ICollectionPipelineAlertPublisher
    {
        public List<(string Reason, int DlqFailureCount)> Calls { get; } = [];

        public Task PublishCollectionStoppedAsync(string reason, int dlqFailureCount, CancellationToken cancellationToken)
        {
            Calls.Add((reason, dlqFailureCount));
            return Task.CompletedTask;
        }
    }
}
