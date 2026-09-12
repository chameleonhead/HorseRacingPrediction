using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionPlatformOutboxDispatcherTests
{
    [TestMethod]
    public async Task CompatibleRaceCards_AreSentInOneStableEnvelope()
    {
        var directory = Path.Combine(Path.GetTempPath(), "platform-envelope", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = directory }));
            await store.RegisterDefinitionAsync(new("race-card"), "card", ResourceType.RaceCard, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var date = new DateOnly(2026, 9, 12);
            for (var index = 12; index >= 1; index--)
                await store.RequestAsync(new(ResourceType.RaceCard, "JRA", $"R{index}"), new("race-card"), 1,
                    CollectionReason.Initial, now.AddSeconds(index), CollectionLane.Realtime, index,
                    effectiveDate: date);
            var queue = new RecordingQueue();
            var dispatcher = new CollectionPlatformOutboxDispatcher(store, queue,
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 1,
                    EnvelopeMaxTasks = 12,
                    AggregationDelayMilliseconds = 0
                }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            await dispatcher.DispatchOnceAsync(CancellationToken.None);

            var envelope = queue.Messages.Single();
            Assert.HasCount(12, envelope.Tasks);
            Assert.AreEqual("JRA", envelope.Compatibility.Provider);
            Assert.AreEqual(new CollectionDefinitionId("race-card"), envelope.Compatibility.Definition);
            Assert.AreEqual(date, envelope.Compatibility.EffectiveDate);
            var priorities = (await store.GetTasksAsync()).ToDictionary(x => x.TaskId, x => x.Priority);
            CollectionAssert.AreEqual(priorities.OrderByDescending(x => x.Value).Select(x => x.Key).ToArray(),
                envelope.Tasks.Select(x => x.TaskId).ToArray());
        }
        finally { Directory.Delete(directory, true); }
    }

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
                Options.Create(new CollectionQueueOptions
                {
                    Enabled = true,
                    DispatchBatchSize = 1,
                    EnvelopeMaxTasks = 1
                }),
                NullLogger<CollectionPlatformOutboxDispatcher>.Instance);

            for (var count = 0; count < 5; count++) await dispatcher.DispatchOnceAsync(CancellationToken.None);

            var tasks = await store.GetTasksAsync();
            var backgroundId = tasks.Single(x => x.Resource.Id == "BACKGROUND").TaskId;
            Assert.AreEqual(backgroundId, queue.Messages[4].Tasks.Single().TaskId);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class RecordingQueue : ICollectionPlatformTaskQueue
    {
        public List<CollectionDispatchEnvelope> Messages { get; } = [];
        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Messages.Add(envelope);
            return Task.FromResult(new CollectionQueueSendReceipt(Guid.NewGuid().ToString("N")));
        }
    }
}
