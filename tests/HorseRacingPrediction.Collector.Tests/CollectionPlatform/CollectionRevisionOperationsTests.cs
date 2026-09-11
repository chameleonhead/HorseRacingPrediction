using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionRevisionOperationsTests
{
    [TestMethod]
    public async Task PreviewSupportsEveryTypedScopeWithoutChangingState()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var oldJra = new ResourceKey(ResourceType.Horse, "JRA", "same");
        var oldOther = new ResourceKey(ResourceType.Horse, "NAR", "same");
        var current = new ResourceKey(ResourceType.Horse, "JRA", "current");
        await SeedCurrentAsync(store, oldJra, new(2009, 1, 1), "legacy");
        await SeedCurrentAsync(store, oldOther, new(2009, 1, 1), "current");
        await SeedCurrentAsync(store, current, new(2020, 1, 1), "current");

        var all = await store.PreviewRevisionImpactAsync(Definition, 8,
            new(RevisionImpactScopeType.All, string.Empty), []);
        var specific = await store.PreviewRevisionImpactAsync(Definition, 9,
            new(RevisionImpactScopeType.SpecificResources,
                System.Text.Json.JsonSerializer.Serialize(new[] { oldJra })), []);
        var range = await store.PreviewRevisionImpactAsync(Definition, 10,
            new(RevisionImpactScopeType.DateRange,
                System.Text.Json.JsonSerializer.Serialize(new { From = new DateOnly(2000, 1, 1), To = new DateOnly(2010, 12, 31) })), []);
        var named = await store.PreviewRevisionImpactAsync(Definition, 11,
            new(RevisionImpactScopeType.NamedCondition, LegacyCondition.ConditionName), [new LegacyCondition()]);

        Assert.AreEqual(3, all.AffectedResources.Count);
        CollectionAssert.AreEquivalent(new[] { oldJra }, specific.AffectedResources.ToArray());
        Assert.AreEqual(2, range.AffectedResources.Count);
        CollectionAssert.AreEquivalent(new[] { oldJra }, named.AffectedResources.ToArray());
        Assert.AreEqual(7, (await store.GetStateAsync(oldJra, Definition))!.RequiredRevision,
            "Preview must not mutate RequiredRevision.");
    }

    [TestMethod]
    public async Task ApplyExpandAndProgressAffectOnlySelectedResourcesAndAreIdempotent()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var affectedSuccess = new ResourceKey(ResourceType.Horse, "JRA", "affected-1");
        var affectedFailure = new ResourceKey(ResourceType.Horse, "JRA", "affected-2");
        var unaffected = new ResourceKey(ResourceType.Horse, "JRA", "unaffected");
        foreach (var resource in new[] { affectedSuccess, affectedFailure, unaffected })
            await SeedCurrentAsync(store, resource, new(2020, 1, 1), "current");
        var impact = new RevisionImpact(RevisionImpactScopeType.SpecificResources,
            System.Text.Json.JsonSerializer.Serialize(new[] { affectedSuccess, affectedFailure }));

        Assert.AreEqual(2, await store.AddRevisionAndApplyImpactAsync(Definition, 8, "selected fix", impact, [], Now));
        Assert.AreEqual(8, (await store.GetStateAsync(affectedSuccess, Definition))!.RequiredRevision);
        Assert.AreEqual(7, (await store.GetStateAsync(unaffected, Definition))!.RequiredRevision);
        Assert.AreEqual(CollectionStateStatus.Current, (await store.GetStateAsync(unaffected, Definition))!.Status);

        var expansion = await store.ExpandRevisionRecollectionAsync(Definition, 8, [], Now.AddMinutes(1));
        Assert.AreEqual(2, expansion.RequestsCreated);
        var repeated = await store.ExpandRevisionRecollectionAsync(Definition, 8, [], Now.AddMinutes(2));
        Assert.AreEqual(0, repeated.RequestsCreated);
        Assert.AreEqual(2, repeated.ExistingRequests);

        var tasks = await store.GetTasksAsync();
        var successTask = tasks.Single(x => x.Resource == affectedSuccess.Normalize() && x.RequestedRevision == 8);
        var failureTask = tasks.Single(x => x.Resource == affectedFailure.Normalize() && x.RequestedRevision == 8);
        await CompleteAsync(store, successTask.TaskId, CollectionAttemptResult.Succeeded);
        await CompleteAsync(store, failureTask.TaskId, CollectionAttemptResult.PermanentFailure);

        var progress = await store.GetRevisionRecollectionProgressAsync(Definition, 8, []);
        Assert.AreEqual(2, progress.Affected);
        Assert.AreEqual(1, progress.Completed);
        Assert.AreEqual(0, progress.Pending);
        Assert.AreEqual(1, progress.Failed);
    }

    [TestMethod]
    public async Task ExpansionWhileOlderRevisionIsRunningCreatesFollowUpAfterCompletion()
    {
        using var directory = new TemporaryDirectory();
        var store = await CreateStoreAsync(directory.Path);
        var resource = new ResourceKey(ResourceType.Horse, "JRA", "running-old-revision");
        var old = await store.RequestAsync(resource, Definition, 7, CollectionReason.Initial, Now);
        var oldLease = await store.AcquireAsync(old.TaskId, 1, Now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(oldLease);
        await store.AddRevisionAndApplyImpactAsync(Definition, 8, "all", new(RevisionImpactScopeType.All, ""), [], Now);

        var expansion = await store.ExpandRevisionRecollectionAsync(Definition, 8, [], Now);
        Assert.AreEqual(0, expansion.RequestsCreated, "The revision request must not create a concurrent active task.");
        Assert.IsTrue(await store.CompleteAttemptAsync(old.TaskId, oldLease.LeaseToken, Now.AddMinutes(1),
            new(CollectionAttemptResult.Succeeded)));

        var tasks = await store.GetTasksAsync();
        Assert.HasCount(2, tasks.Where(x => x.Resource == resource.Normalize()).ToArray());
        Assert.IsTrue(tasks.Any(x => x.RequestedRevision == 8 && x.Status == CollectionTaskStatus.Ready));
        Assert.AreEqual(CollectionStateStatus.Pending, (await store.GetStateAsync(resource, Definition))!.Status);
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
    private static readonly CollectionDefinitionId Definition = new("horse-profile");

    private static async Task<CollectionPlatformStore> CreateStoreAsync(string path)
    {
        var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions { StateDirectory = path }));
        await store.RegisterDefinitionAsync(Definition, "Horse profile", ResourceType.Horse, 7, "baseline", false);
        return store;
    }

    private static async Task SeedCurrentAsync(CollectionPlatformStore store, ResourceKey resource,
        DateOnly date, string layout)
    {
        var receipt = await store.RequestAsync(resource, Definition, 7, CollectionReason.Initial, Now,
            effectiveDate: date, attributes: new Dictionary<string, string> { ["layout"] = layout });
        await CompleteAsync(store, receipt.TaskId, CollectionAttemptResult.Succeeded);
    }

    private static async Task CompleteAsync(CollectionPlatformStore store, Guid taskId, CollectionAttemptResult result)
    {
        var attemptAt = Now.AddHours(1);
        var lease = await store.AcquireAsync(taskId, 1, attemptAt, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(taskId, lease.LeaseToken, attemptAt.AddSeconds(1), new(result)));
    }

    private sealed class LegacyCondition : INamedRevisionImpactCondition
    {
        public const string ConditionName = "horse-profile:legacy-layout";
        public string Name => ConditionName;
        public bool Matches(RevisionResourceCandidate candidate) =>
            candidate.Attributes.GetValueOrDefault("layout") == "legacy";
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"revision-tests-{Guid.NewGuid():N}");
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
