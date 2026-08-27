using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionTaskOutboxDispatcherTests
{
    [TestMethod]
    public async Task DispatchOnceAsync_SendsAndCompletesOutbox()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-dispatch-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var processingOptions = Options.Create(new AgentProcessingOptions { StateDirectory = directory });
            var store = new ProcessingStateStore(processingOptions, NullLogger<ProcessingStateStore>.Instance);
            var now = DateTimeOffset.UtcNow;
            await store.ScheduleJobAsync("RaceCardCollection", "task-1", "{}", now);
            var queue = new RecordingQueue();
            var dispatcher = new CollectionTaskOutboxDispatcher(
                store,
                queue,
                Options.Create(new CollectionQueueOptions { Enabled = true, DispatchBatchSize = 10 }),
                NullLogger<CollectionTaskOutboxDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "task-1" }, queue.Notifications.Select(x => x.DeduplicationKey).ToArray());
            var pending = await store.GetPendingCollectionTaskDispatchesAsync(DateTimeOffset.UtcNow.AddMinutes(1), 10);
            CollectionAssert.AreEqual(Array.Empty<string>(), pending.Select(x => x.OutboxId).ToArray());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingQueue : ICollectionTaskQueue
    {
        public List<CollectionTaskNotification> Notifications { get; } = [];
        public Task SendAsync(CollectionTaskNotification notification, CancellationToken cancellationToken)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
