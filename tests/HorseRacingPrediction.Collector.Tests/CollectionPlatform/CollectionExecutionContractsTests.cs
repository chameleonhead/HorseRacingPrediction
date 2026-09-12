using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;

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

    [TestMethod]
    public async Task LocalExecutor_CancellationIsPersistedAsRetryableWithIndependentCompletionToken()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"local-executor-cancel-{Guid.NewGuid():N}");
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            { StateDirectory = directory }));
            var definition = new CollectionDefinitionId("horse-profile");
            var resource = new ResourceKey(ResourceType.Horse, "JRA", "H-CANCEL");
            await store.RegisterDefinitionAsync(definition, "Horse", ResourceType.Horse, 1, "initial", false);
            var request = await store.RequestAsync(resource, definition, 1, CollectionReason.Initial,
                DateTimeOffset.UtcNow);
            using var cancellation = new CancellationTokenSource();
            var executor = new CollectionTaskExecutor(store,
                new CollectionDefinitionHandlerRegistry([new CancellingHandler(cancellation)]));

            Assert.IsTrue(await executor.ExecuteAsync(new(request.TaskId, 1), DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(5), cancellation.Token));

            var task = (await store.GetTasksAsync()).Single(x => x.TaskId == request.TaskId);
            Assert.AreEqual(CollectionTaskStatus.Ready, task.Status);
            Assert.IsTrue(task.AvailableAt > DateTimeOffset.UtcNow);
            var attempts = await store.GetAttemptsAsync(request.TaskId);
            Assert.AreEqual(CollectionAttemptResult.TransientFailure, attempts.Single().Result);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class Handler(string id, ResourceType type) : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new(id);
        public ResourceType ResourceType => type;
        public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task, CancellationToken cancellationToken)
            => Task.FromResult(new CollectionAttemptCompletion(CollectionAttemptResult.Succeeded));
    }

    private sealed class CancellingHandler(CancellationTokenSource cancellation) : ICollectionDefinitionHandler
    {
        public CollectionDefinitionId DefinitionId => new("horse-profile");
        public ResourceType ResourceType => ResourceType.Horse;
        public Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }
    }
}
