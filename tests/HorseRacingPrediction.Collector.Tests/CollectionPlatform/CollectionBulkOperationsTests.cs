using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionBulkOperationsTests
{
    [TestMethod]
    public async Task ValidationFailureCreatesNoRequestTaskOrState()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var valid = new CollectionBulkTarget(new(ResourceType.Horse, "JRA", "H1"));
        var invalid = new CollectionBulkTarget(new(ResourceType.Trainer, "JRA", "T1"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.ExecuteBulkRequestAsync(
            Definition, 1, CollectionReason.ManualRefresh, [valid, invalid], Now, "invalid-batch",
            CollectionLane.Normal, 50));

        Assert.IsEmpty(await store.GetTasksAsync());
        Assert.IsNull(await store.GetStateAsync(valid.Resource, Definition));
    }

    [TestMethod]
    public async Task PreviewIsReadOnlyAndExecutionIsAtomicAndIdempotent()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var targets = new[]
        {
            new CollectionBulkTarget(new ResourceKey(ResourceType.Horse, "jra", "H1")),
            new CollectionBulkTarget(new ResourceKey(ResourceType.Horse, "JRA", "H2")),
        };

        var preview = await store.PreviewBulkRequestAsync(Definition, 1, targets);
        Assert.AreEqual(2, preview.TargetCount);
        Assert.IsEmpty(await store.GetTasksAsync());
        var first = await store.ExecuteBulkRequestAsync(Definition, 1, CollectionReason.ManualRefresh,
            targets, Now, "manual:test", CollectionLane.Normal, 50);
        var second = await store.ExecuteBulkRequestAsync(Definition, 1, CollectionReason.ManualRefresh,
            targets, Now.AddMinutes(1), "manual:test", CollectionLane.Normal, 50);

        Assert.AreEqual(2, first.TasksCreated);
        Assert.AreEqual(0, second.TasksCreated);
        Assert.HasCount(2, await store.GetTasksAsync());
    }

    [TestMethod]
    public async Task StateAndLastCollectedSelectorsReturnOnlyMatchingResources()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var failed = new ResourceKey(ResourceType.Horse, "JRA", "failed");
        var current = new ResourceKey(ResourceType.Horse, "JRA", "current");
        await FinishAsync(store, failed, CollectionAttemptResult.PermanentFailure, Now);
        await FinishAsync(store, current, CollectionAttemptResult.Succeeded, Now.AddDays(2));

        var failedTargets = await store.SelectBulkTargetsAsync(Definition, status: CollectionStateStatus.Failed);
        var oldTargets = await store.SelectBulkTargetsAsync(Definition, lastCollectedBefore: Now.AddDays(1));
        CollectionAssert.AreEquivalent(new[] { failed.Normalize() }, failedTargets.Select(x => x.Resource).ToArray());
        CollectionAssert.AreEquivalent(new[] { failed.Normalize() }, oldTargets.Select(x => x.Resource).ToArray());
        await store.AddRevisionAndApplyImpactAsync(Definition, 2, "current only",
            new(RevisionImpactScopeType.SpecificResources,
                System.Text.Json.JsonSerializer.Serialize(new[] { current })), [], Now.AddDays(3));
        var staleTargets = await store.SelectBulkTargetsAsync(Definition, status: CollectionStateStatus.Stale);
        CollectionAssert.AreEquivalent(new[] { current.Normalize() }, staleTargets.Select(x => x.Resource).ToArray());
    }

    private static readonly CollectionDefinitionId Definition = new("horse-profile");
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
    private static async Task<CollectionPlatformStore> CreateStoreAsync(string path)
    {
        var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = path }));
        await store.RegisterDefinitionAsync(Definition, "Horse", ResourceType.Horse, 1, "initial", false);
        return store;
    }
    private static async Task FinishAsync(CollectionPlatformStore store, ResourceKey resource,
        CollectionAttemptResult result, DateTimeOffset finishedAt)
    {
        var receipt = await store.RequestAsync(resource, Definition, 1, CollectionReason.Initial, Now);
        var lease = await store.AcquireAsync(receipt.TaskId, 1, Now, TimeSpan.FromDays(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, finishedAt, new(result)));
    }
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bulk-tests-{Guid.NewGuid():N}");
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
