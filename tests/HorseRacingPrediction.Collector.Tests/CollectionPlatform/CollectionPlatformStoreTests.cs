using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionPlatformStoreTests
{
    private string _directory = null!;
    private static readonly CollectionDefinitionId HorseProfile = new("horse-profile");
    private static readonly ResourceKey Horse = new(ResourceType.Horse, "jra", "H123");

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "collection-platform-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task SameResourceAndRevision_CanBeCollectedMultipleTimesAfterCompletion()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(first.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(first.TaskId, lease.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.Succeeded)));

        var second = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now.AddDays(1));

        Assert.IsTrue(second.CreatedTask);
        Assert.AreNotEqual(first.TaskId, second.TaskId);
        var state = await store.GetStateAsync(Horse, HorseProfile);
        Assert.IsNotNull(state);
        Assert.AreEqual(7, state.AppliedRevision);
        Assert.AreEqual(CollectionStateStatus.Pending, state.Status);
    }

    [TestMethod]
    public async Task SameResourceAndDefinition_ReusesActiveTaskButRetainsEachRequest()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;

        var initial = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var manual = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now.AddSeconds(1));

        Assert.IsTrue(initial.CreatedTask);
        Assert.IsFalse(manual.CreatedTask);
        Assert.AreEqual(initial.TaskId, manual.TaskId);
        Assert.AreNotEqual(initial.RequestId, manual.RequestId);
    }

    [TestMethod]
    public async Task TransientFailure_RetriesSameTaskAndPreservesAttemptHistoryAcrossRestart()
    {
        var now = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var first = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(first);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, first.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.TransientFailure, "Http503", HttpStatusCode: 503,
                RetryAt: now.AddMinutes(2))));

        var restarted = CreateStore();
        var second = await restarted.AcquireAsync(receipt.TaskId, 2, now.AddMinutes(2), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(second);
        Assert.IsTrue(await restarted.CompleteAttemptAsync(receipt.TaskId, second.LeaseToken, now.AddMinutes(3),
            new(CollectionAttemptResult.Succeeded, RequestedUrl: new("https://example.test/horse/H123"),
                PageIdentification: "Horse:JRA:H123")));

        var attempts = await restarted.GetAttemptsAsync(receipt.TaskId);
        Assert.HasCount(2, attempts);
        Assert.AreEqual(CollectionAttemptResult.TransientFailure, attempts[0].Result);
        Assert.AreEqual(503, attempts[0].HttpStatusCode);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, attempts[1].Result);
        Assert.AreEqual("Horse:JRA:H123", attempts[1].PageIdentification);
    }

    [TestMethod]
    public async Task ExpiredLease_IsRecoveredWithNewDispatchGeneration()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery, now);
        Assert.IsNotNull(await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(1)));

        var recovered = await CreateStore().AcquireAsync(receipt.TaskId, 2, now.AddMinutes(2), TimeSpan.FromMinutes(1));

        Assert.IsNotNull(recovered);
        var attempts = await store.GetAttemptsAsync(receipt.TaskId);
        Assert.HasCount(2, attempts);
        Assert.AreEqual("LeaseExpired", attempts[0].ErrorCode);
    }

    [TestMethod]
    public async Task RevisionImpact_UpdatesOnlyMatchingResources()
    {
        var now = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);
        var store = await CreateStoreAsync();
        var other = new ResourceKey(ResourceType.Horse, "JRA", "H999");
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now,
            effectiveDate: new DateOnly(2009, 1, 1), attributes: new Dictionary<string, string> { ["layout"] = "legacy" });
        var second = await store.RequestAsync(other, HorseProfile, 7, CollectionReason.Initial, now,
            effectiveDate: new DateOnly(2020, 1, 1), attributes: new Dictionary<string, string> { ["layout"] = "current" });
        await CompleteAsync(store, first, now);
        await CompleteAsync(store, second, now);

        var affected = await store.AddRevisionAndApplyImpactAsync(HorseProfile, 8, "Legacy layout fix",
            new(RevisionImpactScopeType.NamedCondition, "horse-profile:legacy-layout"),
            [new LegacyLayoutCondition()], now.AddDays(1));

        Assert.AreEqual(1, affected);
        var stale = await store.GetStateAsync(Horse, HorseProfile);
        var current = await store.GetStateAsync(other, HorseProfile);
        Assert.AreEqual(CollectionStateStatus.Stale, stale!.Status);
        Assert.AreEqual(8, stale.RequiredRevision);
        Assert.AreEqual(CollectionStateStatus.Current, current!.Status);
        Assert.AreEqual(7, current.RequiredRevision);
    }

    [TestMethod]
    public async Task RevisionImpact_RejectsUnknownNamedCondition()
    {
        var store = await CreateStoreAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.AddRevisionAndApplyImpactAsync(
            HorseProfile, 8, "Unknown selector", new(RevisionImpactScopeType.NamedCondition, "unknown"),
            [], DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public async Task TransientLocationFailure_DoesNotInvalidateLocation()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var id = await store.UpsertLocationAsync(Horse, HorseProfile, new("https://example.test/horse/H123"),
            ResourceLocationSource.Discovered, now);

        await store.RecordLocationOutcomeAsync(id, CollectionAttemptResult.TransientFailure, now.AddMinutes(1), "Http503");

        var locations = await store.ResolveLocationsAsync(Horse, HorseProfile);
        Assert.HasCount(1, locations);
        Assert.AreEqual(ResourceLocationStatus.Unknown, locations[0].Status);
    }

    [TestMethod]
    public async Task UnexpectedPage_MarksLocationSuspectAndSuccessVerifiesIt()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var id = await store.UpsertLocationAsync(Horse, HorseProfile, new("https://example.test/horse/H123"),
            ResourceLocationSource.Generated, now);

        await store.RecordLocationOutcomeAsync(id, CollectionAttemptResult.UnexpectedPage, now.AddMinutes(1));
        Assert.AreEqual(ResourceLocationStatus.Suspect, (await store.ResolveLocationsAsync(Horse, HorseProfile))[0].Status);
        await store.RecordLocationOutcomeAsync(id, CollectionAttemptResult.Succeeded, now.AddMinutes(2));
        Assert.AreEqual(ResourceLocationStatus.Active, (await store.ResolveLocationsAsync(Horse, HorseProfile))[0].Status);
    }

    private async Task<CollectionPlatformStore> CreateStoreAsync()
    {
        var store = CreateStore();
        await store.RegisterDefinitionAsync(HorseProfile, "Horse profile", ResourceType.Horse, 7,
            "Initial profile extractor", false);
        return store;
    }

    private CollectionPlatformStore CreateStore() => new(Options.Create(new CollectionPlatformOptions
    {
        StateDirectory = _directory,
    }));

    private static async Task CompleteAsync(CollectionPlatformStore store, CollectionRequestReceipt receipt,
        DateTimeOffset now)
    {
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.Succeeded)));
    }

    private sealed class LegacyLayoutCondition : INamedRevisionImpactCondition
    {
        public string Name => "horse-profile:legacy-layout";
        public bool Matches(RevisionResourceCandidate candidate)
            => candidate.Attributes.GetValueOrDefault("layout") == "legacy";
    }
}
