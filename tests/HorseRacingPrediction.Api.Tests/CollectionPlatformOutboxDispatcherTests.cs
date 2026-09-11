using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionPlatformOutboxDispatcherTests
{
    [TestMethod]
    public async Task ActualDispatcher_PrioritizesRealtimeButDispatchesBackgroundAfterFour()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-dispatch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-card"), "card", ResourceType.RaceCard, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            for (var index = 0; index < 5; index++)
                await store.RequestAsync(new(ResourceType.RaceCard, "JRA", $"R{index}"), new("race-card"), 1,
                    CollectionReason.Initial, now, CollectionLane.Realtime, 100);
            await store.RequestAsync(new(ResourceType.RaceCard, "JRA", "BACKGROUND"), new("race-card"), 1,
                CollectionReason.Backfill, now, CollectionLane.Background, 10);
            var queue = new RecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions { Enabled = true, DispatchBatchSize = 1 }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            for (var count = 0; count < 5; count++) await dispatcher.DispatchOnceAsync(CancellationToken.None);

            var tasks = await store.GetTasksAsync();
            var backgroundId = tasks.Single(x => x.Resource.Id == "BACKGROUND").TaskId;
            Assert.AreEqual(backgroundId, queue.Messages[4].TaskId);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class RecordingQueue : ICollectionPlatformTaskQueue
    {
        public List<CollectionTaskNotification> Messages { get; } = [];
        public Task SendAsync(CollectionTaskNotification notification, CancellationToken cancellationToken)
        {
            Messages.Add(notification);
            return Task.CompletedTask;
        }
    }
}
