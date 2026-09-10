using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionExecutionContractsTests
{
    [TestMethod]
    public void Registry_RejectsDuplicateDefinitions()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new CollectionDefinitionHandlerRegistry(
            [new Handler("horse-profile", ResourceType.Horse), new Handler("horse-profile", ResourceType.Horse)]));
    }

    [TestMethod]
    public void Registry_RejectsResourceTypeMismatch()
    {
        var registry = new CollectionDefinitionHandlerRegistry([new Handler("horse-profile", ResourceType.Horse)]);

        Assert.ThrowsExactly<InvalidOperationException>(() => registry.Resolve(new("horse-profile"), ResourceType.Trainer));
    }

    [TestMethod]
    public void Allocator_PrioritizesRealtime()
    {
        var now = DateTimeOffset.UtcNow;
        var realtime = new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Realtime, 90, now, now);
        var background = new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Background, 100, now, now);

        Assert.AreEqual(realtime.TaskId, new CollectionLaneAllocator().Select([background, realtime], now)!.TaskId);
    }

    [TestMethod]
    public void Allocator_DoesNotStarveBackground()
    {
        var now = DateTimeOffset.UtcNow;
        var allocator = new CollectionLaneAllocator(maxConsecutiveRealtime: 2);
        var background = new FairCollectionCandidate(Guid.NewGuid(), CollectionLane.Background, 10, now, now);
        FairCollectionCandidate Realtime() => new(Guid.NewGuid(), CollectionLane.Realtime, 100, now, now);

        Assert.AreEqual(CollectionLane.Realtime, allocator.Select([background, Realtime()], now)!.Lane);
        Assert.AreEqual(CollectionLane.Realtime, allocator.Select([background, Realtime()], now)!.Lane);
        Assert.AreEqual(CollectionLane.Background, allocator.Select([background, Realtime()], now)!.Lane);
    }

    private sealed class Handler(string id, ResourceType type) : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new(id);
        public ResourceType ResourceType => type;
        public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken cancellationToken)
            => Task.FromResult(new CollectionAttemptCompletion(CollectionAttemptResult.Succeeded));
    }
}
