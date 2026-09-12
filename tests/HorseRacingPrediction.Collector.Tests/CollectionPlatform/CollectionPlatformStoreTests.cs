using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    public async Task SearchTasks_FiltersAndPagesWithAnExactTotalCount()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        await store.RequestAsync(new(ResourceType.Horse, "JRA", "H001"), HorseProfile, 7,
            CollectionReason.Initial, now, CollectionLane.Background, 10);
        await store.RequestAsync(new(ResourceType.Horse, "JRA", "H002"), HorseProfile, 7,
            CollectionReason.Initial, now.AddMinutes(1), CollectionLane.Normal, 50);
        await store.RequestAsync(new(ResourceType.Horse, "NAR", "H003"), HorseProfile, 7,
            CollectionReason.Initial, now.AddMinutes(2), CollectionLane.Normal, 50);

        var firstPage = await store.SearchTasksAsync(new(ResourceType: ResourceType.Horse,
            Provider: "jra", DefinitionId: "horse-profile", Page: 1, PageSize: 1));
        var secondPage = await store.SearchTasksAsync(new(ResourceType: ResourceType.Horse,
            Provider: "JRA", DefinitionId: "horse-profile", Page: 2, PageSize: 1));
        var searched = await store.SearchTasksAsync(new(Search: "H002"));
        var recent = await store.SearchTasksAsync(new(CreatedFrom: now.AddSeconds(30)));

        Assert.AreEqual(2, firstPage.TotalCount);
        Assert.HasCount(1, firstPage.Items);
        Assert.AreEqual("H002", firstPage.Items[0].Resource.Id);
        Assert.AreEqual("H001", secondPage.Items[0].Resource.Id);
        Assert.AreEqual(1, searched.TotalCount);
        Assert.AreEqual("H002", searched.Items.Single().Resource.Id);
        Assert.AreEqual(2, recent.TotalCount);
    }

    [TestMethod]
    public async Task Request_RejectsRevisionNotRegisteredByDefinition()
    {
        var store = await CreateStoreAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.RequestAsync(
            Horse, HorseProfile, 999, CollectionReason.ManualRefresh, DateTimeOffset.UtcNow));
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

        var restarted = CreateStore();
        Assert.AreEqual(1, await restarted.ReclaimExpiredLeasesAsync(now.AddMinutes(2)));
        var recovered = await restarted.AcquireAsync(receipt.TaskId, 2, now.AddMinutes(2), TimeSpan.FromMinutes(1));

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
    public async Task SuccessfulRepeatedObservation_BecomesDueAtNextCollectionTime()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        var next = now.AddMinutes(10);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.Succeeded, NextCollectionAt: next)));

        Assert.IsEmpty(await store.GetDueStatesAsync(next.AddTicks(-1)));
        var due = await store.GetDueStatesAsync(next);
        Assert.HasCount(1, due);
        Assert.AreEqual(Horse.Normalize(), due[0].Resource);
    }

    [TestMethod]
    public async Task SuccessfulExplicitUrl_IsPromotedToVerifiedResourceLocation()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var url = new Uri("https://example.test/horse/H123");
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now,
            explicitUrl: url);
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.AreEqual(url, lease.Locations![0].Url);
        await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded, RequestedUrl: url, FinalUrl: url));

        var locations = await store.ResolveLocationsAsync(Horse, HorseProfile);
        Assert.HasCount(1, locations);
        Assert.AreEqual(ResourceLocationStatus.Active, locations[0].Status);
        Assert.IsNotNull(locations[0].LastVerifiedAt);
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

    [TestMethod]
    public async Task CompleteAttempt_RecordsEveryCandidateOutcomeAtomically()
    {
        var now = DateTimeOffset.UtcNow;
        var store = CreateStore();
        await store.RegisterDefinitionAsync(new("race-card"), "Race card", ResourceType.RaceCard, 1,
            "Initial", false);
        var resource = new ResourceKey(ResourceType.RaceCard, "JRA", "R1");
        var receipt = await store.RequestAsync(resource, new("race-card"), 1, CollectionReason.Initial, now,
            CollectionLane.Realtime, 80);
        var first = await store.UpsertLocationAsync(resource, new("race-card"),
            new Uri("https://example.test/wrong"), ResourceLocationSource.Discovered, now);
        var second = await store.UpsertLocationAsync(resource, new("race-card"),
            new Uri("https://example.test/card"), ResourceLocationSource.Discovered, now);
        var transient = await store.UpsertLocationAsync(resource, new("race-card"),
            new Uri("https://example.test/temporarily-unavailable"), ResourceLocationSource.Discovered, now);
        var limited = await store.UpsertLocationAsync(resource, new("race-card"),
            new Uri("https://example.test/rate-limited"), ResourceLocationSource.Discovered, now);
        var missing = await store.UpsertLocationAsync(resource, new("race-card"),
            new Uri("https://example.test/missing"), ResourceLocationSource.Discovered, now);
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));

        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes:
            [
                new(first, CollectionAttemptResult.UnexpectedPage, "RaceIdMismatch"),
                new(transient, CollectionAttemptResult.TransientFailure, "Http503"),
                new(limited, CollectionAttemptResult.AccessLimited, "Http429"),
                new(missing, CollectionAttemptResult.ResourceNotFound, "Http404"),
                new(second, CollectionAttemptResult.Succeeded),
            ])));

        var locations = await store.ResolveLocationsAsync(resource, new("race-card"));
        Assert.AreEqual(ResourceLocationStatus.Active, locations.Single(x => x.LocationId == second).Status);
        Assert.AreEqual(ResourceLocationStatus.Suspect, locations.Single(x => x.LocationId == first).Status);
        Assert.AreEqual(ResourceLocationStatus.Unknown, locations.Single(x => x.LocationId == transient).Status);
        Assert.AreEqual(ResourceLocationStatus.Unknown, locations.Single(x => x.LocationId == limited).Status);
        Assert.AreEqual(ResourceLocationStatus.Suspect, locations.Single(x => x.LocationId == missing).Status);
    }

    [TestMethod]
    public async Task CompleteAttempt_RejectsForeignOrMissingLocationWithoutChangingTaskOrLocations()
    {
        var now = DateTimeOffset.UtcNow;
        var store = CreateStore();
        var definition = new CollectionDefinitionId("race-card");
        await store.RegisterDefinitionAsync(definition, "Race card", ResourceType.RaceCard, 1,
            "Initial", false);
        var resource = new ResourceKey(ResourceType.RaceCard, "JRA", "R1");
        var foreignResource = new ResourceKey(ResourceType.RaceCard, "JRA", "R2");
        var receipt = await store.RequestAsync(resource, definition, 1, CollectionReason.Initial, now);
        await store.RequestAsync(foreignResource, definition, 1, CollectionReason.Initial, now);
        var own = await store.UpsertLocationAsync(resource, definition, new("https://example.test/r1"),
            ResourceLocationSource.Discovered, now);
        var foreign = await store.UpsertLocationAsync(foreignResource, definition, new("https://example.test/r2"),
            ResourceLocationSource.Discovered, now);
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);

        Assert.IsFalse(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes:
            [new(own, CollectionAttemptResult.Succeeded), new(foreign, CollectionAttemptResult.UnexpectedPage)])));
        Assert.IsFalse(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(2),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes:
            [new(own, CollectionAttemptResult.Succeeded), new(long.MaxValue, CollectionAttemptResult.UnexpectedPage)])));

        Assert.AreEqual(ResourceLocationStatus.Unknown,
            (await store.ResolveLocationsAsync(resource, definition)).Single().Status);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(3),
            new(CollectionAttemptResult.Succeeded,
                LocationOutcomes: [new(own, CollectionAttemptResult.Succeeded)])));
    }

    [TestMethod]
    public async Task CompleteAttempt_RejectsConflictingOrExcessiveOutcomesButAcceptsExactDuplicates()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var location = await store.UpsertLocationAsync(Horse, HorseProfile,
            new("https://example.test/horse/H123"), ResourceLocationSource.Discovered, now);
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);

        Assert.IsFalse(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes:
            [
                new(location, CollectionAttemptResult.Succeeded),
                new(location, CollectionAttemptResult.UnexpectedPage, "RaceIdMismatch"),
            ])));
        Assert.IsFalse(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(2),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes: Enumerable.Range(0, 101)
                .Select(_ => new ResourceLocationOutcome(location, CollectionAttemptResult.Succeeded)).ToList())));

        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(3),
            new(CollectionAttemptResult.Succeeded, LocationOutcomes:
            [
                new(location, CollectionAttemptResult.Succeeded),
                new(location, CollectionAttemptResult.Succeeded),
            ])));
        Assert.AreEqual(ResourceLocationStatus.Active,
            (await store.ResolveLocationsAsync(Horse, HorseProfile)).Single().Status);
    }

    [TestMethod]
    public async Task CompleteAttempt_WrongLeaseAndRedeliveryCannotOverwriteCompletedOutcome()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var location = await store.UpsertLocationAsync(Horse, HorseProfile,
            new("https://example.test/horse/H123"), ResourceLocationSource.Discovered, now);
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);

        Assert.IsFalse(await store.CompleteAttemptAsync(receipt.TaskId, "wrong-lease", now.AddSeconds(1),
            new(CollectionAttemptResult.UnexpectedPage,
                LocationOutcomes: [new(location, CollectionAttemptResult.UnexpectedPage)])));
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(2),
            new(CollectionAttemptResult.Succeeded,
                LocationOutcomes: [new(location, CollectionAttemptResult.Succeeded)])));
        Assert.IsFalse(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(3),
            new(CollectionAttemptResult.UnexpectedPage,
                LocationOutcomes: [new(location, CollectionAttemptResult.UnexpectedPage)])));

        var state = await store.GetStateAsync(Horse, HorseProfile);
        Assert.AreEqual(CollectionStateStatus.Current, state?.Status);
        Assert.AreEqual(ResourceLocationStatus.Active,
            (await store.ResolveLocationsAsync(Horse, HorseProfile)).Single().Status);
    }

    [TestMethod]
    public async Task Startup_BaselinesEnsureCreatedDatabaseWithoutLosingExistingData()
    {
        var databasePath = Path.Combine(_directory, "collection-platform.db");
        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options;
        await using (var legacy = new CollectionPlatformDbContext(options))
        {
            await legacy.Database.EnsureCreatedAsync();
            legacy.Definitions.Add(new CollectionDefinitionEntity
            {
                DefinitionId = HorseProfile.Value,
                Name = "Existing definition",
                ResourceType = ResourceType.Horse,
                CurrentRevision = 7,
                Enabled = true
            });
            legacy.Revisions.Add(new CollectionRevisionEntity
            {
                DefinitionId = HorseProfile.Value,
                Revision = 7,
                Description = "Existing revision",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await legacy.SaveChangesAsync();
        }

        _ = CreateStore();

        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var history = connection.CreateCommand();
        history.CommandText = "SELECT MAX(version) FROM collection_schema_history;";
        Assert.AreEqual(6L, (long)(await history.ExecuteScalarAsync())!);
        await using var existing = connection.CreateCommand();
        existing.CommandText = "SELECT Name FROM collection_definitions WHERE DefinitionId = 'horse-profile';";
        Assert.AreEqual("Existing definition", await existing.ExecuteScalarAsync());
    }

    [TestMethod]
    public async Task Startup_RejectsIncompleteUnversionedDatabase()
    {
        var databasePath = Path.Combine(_directory, "collection-platform.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE collection_resources (ResourcePk INTEGER PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => CreateStore());
        StringAssert.Contains(error.Message, "incomplete schema");
    }

    [TestMethod]
    public async Task TransientFailure_WithoutExplicitRetryAt_UsesDefaultBackoff()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);

        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now,
            new(CollectionAttemptResult.TransientFailure, "Http503")));

        var task = (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId);
        Assert.AreEqual(CollectionTaskStatus.Ready, task.Status);
        Assert.IsTrue(task.AvailableAt > now);
    }

    [TestMethod]
    public async Task Pause_PersistsAcrossRestartAndBlocksAcquisitionUntilResume()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        await store.SetPausedAsync(true, "maintenance", now);

        var restarted = CreateStore();
        Assert.IsTrue((await restarted.GetPipelineStateAsync()).IsPaused);
        Assert.IsNull(await restarted.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5)));

        await restarted.SetPausedAsync(false, null, now.AddMinutes(1));
        Assert.IsNotNull(await restarted.AcquireAsync(receipt.TaskId, 1, now.AddMinutes(1), TimeSpan.FromMinutes(5)));
    }

    [TestMethod]
    public async Task CancelPendingTask_RemovesActiveGuardAndSuppressesOutbox()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);

        Assert.IsTrue(await store.CancelTaskAsync(receipt.TaskId, now.AddSeconds(1)));

        Assert.AreEqual(CollectionTaskStatus.Cancelled,
            (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId).Status);
        Assert.IsFalse(await store.HasActiveTaskAsync(Horse, HorseProfile));
        Assert.IsEmpty(await store.GetPendingDispatchesAsync(now.AddMinutes(1), 10));
    }

    [TestMethod]
    public async Task DeadLetter_CurrentGenerationFailsTaskAndQueuesNotification()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);

        Assert.IsFalse(await store.ReconcileDeadLetterAsync(receipt.TaskId, 0, now));
        Assert.IsTrue(await store.ReconcileDeadLetterAsync(receipt.TaskId, 1, now, "lambda failed"));

        Assert.AreEqual(CollectionTaskStatus.DeadLetter,
            (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId).Status);
        var notifications = await store.GetPendingFailureNotificationsAsync(now.AddSeconds(1), 10);
        Assert.HasCount(1, notifications);
        Assert.AreEqual("DeadLetterQueue", notifications[0].ErrorCode);
    }

    [TestMethod]
    public async Task Watchdog_RedispatchesStalledReadyTaskThenDeadLettersAtLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var firstOutbox = (await store.GetPendingDispatchesAsync(now, 10)).Single();
        await store.MarkDispatchedAsync(firstOutbox.OutboxId, now);

        var recovered = await store.RunWatchdogAsync(now.AddMinutes(2), 2, TimeSpan.FromMinutes(1));
        Assert.AreEqual(1, recovered.RedispatchedTasks);
        var secondOutbox = (await store.GetPendingDispatchesAsync(now.AddMinutes(2), 10)).Single();
        await store.MarkDispatchedAsync(secondOutbox.OutboxId, now.AddMinutes(2));

        var exhausted = await store.RunWatchdogAsync(now.AddMinutes(4), 2, TimeSpan.FromMinutes(1));
        Assert.AreEqual(1, exhausted.DeadLetteredTasks);
        Assert.AreEqual(CollectionTaskStatus.DeadLetter,
            (await store.GetTasksAsync()).Single(x => x.TaskId == receipt.TaskId).Status);
    }

    [TestMethod]
    public async Task Backfill_RestartsWithoutDuplicateTasksAndProjectsHolesWhileLaterBatchContinues()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", ResourceType.Race,
            1, "initial", false);
        var first = await store.CreateOrResumeBackfillBatchAsync("jra:2026-01", "jra",
            new(2026, 1, 1), new(2026, 1, 3), now);
        Assert.AreEqual(3, first.RegisteredDiscoveryDays);

        var tasks = await store.GetTasksAsync();
        var day1 = tasks.Single(x => x.Resource.Id == "backfill:20260101");
        var failedLease = await store.AcquireAsync(day1.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(failedLease);
        await store.CompleteAttemptAsync(day1.TaskId, failedLease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "BrokenDay", "fixture failure"));
        var day2 = tasks.Single(x => x.Resource.Id == "backfill:20260102");
        var successLease = await store.AcquireAsync(day2.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(successLease);
        await store.CompleteAttemptAsync(day2.TaskId, successLease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded));

        var restarted = CreateStore();
        var resumed = await restarted.CreateOrResumeBackfillBatchAsync("jra:2026-01", "jra",
            new(2026, 1, 1), new(2026, 1, 3), now.AddMinutes(1));
        Assert.AreEqual(3, resumed.RegisteredDiscoveryDays);
        Assert.AreEqual(1, resumed.Failed);
        Assert.AreEqual(1, resumed.Succeeded);
        Assert.AreEqual(1, resumed.Pending);
        Assert.HasCount(1, resumed.Holes);
        Assert.AreEqual("backfill:20260101", resumed.Holes[0].Resource.Id);
        Assert.AreEqual("BrokenDay", resumed.Holes[0].ErrorCode);

        var later = await restarted.CreateOrResumeBackfillBatchAsync("jra:2026-02", "jra",
            new(2026, 2, 1), new(2026, 2, 2), now.AddMinutes(2));
        Assert.AreEqual(2, later.RegisteredDiscoveryDays);
        Assert.AreEqual(2, later.Pending);
    }

    [TestMethod]
    public async Task Backfill_ResolvedRecoveryIsRemovedFromProjectedHoles()
    {
        var now = DateTimeOffset.UtcNow;
        var store = await CreateStoreAsync();
        var discovery = new CollectionDefinitionId("race-discovery");
        await store.RegisterDefinitionAsync(discovery, "Race discovery", ResourceType.Race,
            1, "initial", false);
        await store.CreateOrResumeBackfillBatchAsync("jra:recovery", "jra",
            new(2026, 1, 1), new(2026, 1, 1), now);
        var failedTask = (await store.GetTasksAsync()).Single(x => x.Resource.Id == "backfill:20260101");
        var failedLease = await store.AcquireAsync(failedTask.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(failedLease);
        await store.CompleteAttemptAsync(failedTask.TaskId, failedLease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "BrokenDay"));
        Assert.HasCount(1, (await store.GetBackfillBatchAsync("jra:recovery"))!.Holes);

        var recovery = await store.RequestAsync(failedTask.Resource, discovery, 1, CollectionReason.Recovery,
            now.AddMinutes(1), CollectionLane.Background, (int)CollectionPriority.Background,
            batchId: "recovery:jra:recovery");
        await CompleteAsync(store, recovery, now.AddMinutes(1));

        var recovered = await store.GetBackfillBatchAsync("jra:recovery");
        Assert.IsNotNull(recovered);
        Assert.AreEqual(0, recovered.Failed);
        Assert.IsEmpty(recovered.Holes);
    }

    [TestMethod]
    public async Task Backfill_RecoveryAtSameTimestampStillResolvesHole()
    {
        var now = DateTimeOffset.UtcNow;
        var store = CreateStore();
        var discovery = new CollectionDefinitionId("race-discovery");
        await store.RegisterDefinitionAsync(discovery, "Race discovery", ResourceType.Race,
            1, "initial", false);
        await store.CreateOrResumeBackfillBatchAsync("jra:same-time", "jra",
            new(2026, 1, 1), new(2026, 1, 1), now);
        var failedTask = (await store.GetTasksAsync()).Single();
        var failedLease = await store.AcquireAsync(failedTask.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(failedLease);
        await store.CompleteAttemptAsync(failedTask.TaskId, failedLease.LeaseToken, now,
            new(CollectionAttemptResult.PermanentFailure, "BrokenDay"));

        var recovery = await store.RequestAsync(failedTask.Resource, discovery, 1, CollectionReason.Recovery,
            now, CollectionLane.Background, (int)CollectionPriority.Background,
            batchId: "recovery:jra:same-time:1");
        var recoveryLease = await store.AcquireAsync(recovery.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(recoveryLease);
        Assert.IsTrue(await store.CompleteAttemptAsync(recovery.TaskId, recoveryLease.LeaseToken, now,
            new(CollectionAttemptResult.Succeeded)));

        Assert.IsEmpty((await store.GetBackfillBatchAsync("jra:same-time"))!.Holes);
    }

    [TestMethod]
    public async Task Backfill_OlderRecoverySuccessDoesNotHideLaterFailure()
    {
        var now = DateTimeOffset.UtcNow;
        var store = CreateStore();
        var discovery = new CollectionDefinitionId("race-discovery");
        await store.RegisterDefinitionAsync(discovery, "Race discovery", ResourceType.Race,
            1, "initial", false);
        await store.CreateOrResumeBackfillBatchAsync("jra:ordered", "jra",
            new(2026, 1, 1), new(2026, 1, 1), now);
        var failedTask = (await store.GetTasksAsync()).Single();
        var failedLease = await store.AcquireAsync(failedTask.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(failedLease);
        await store.CompleteAttemptAsync(failedTask.TaskId, failedLease.LeaseToken, now,
            new(CollectionAttemptResult.PermanentFailure, "BrokenDay"));

        var recovery = await store.RequestAsync(failedTask.Resource, discovery, 1, CollectionReason.Recovery,
            now.AddMinutes(-1), CollectionLane.Background, (int)CollectionPriority.Background,
            batchId: "recovery:jra:ordered:old");
        var recoveryLease = await store.AcquireAsync(recovery.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(recoveryLease);
        Assert.IsTrue(await store.CompleteAttemptAsync(recovery.TaskId, recoveryLease.LeaseToken, now,
            new(CollectionAttemptResult.Succeeded)));

        Assert.HasCount(1, (await store.GetBackfillBatchAsync("jra:ordered"))!.Holes);
    }

    [TestMethod]
    public async Task BackfillList_OrdersBatchesWithoutSqliteDateTimeOffsetOrdering()
    {
        var store = CreateStore();
        await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", ResourceType.Race,
            1, "Initial", false);
        await store.CreateOrResumeBackfillBatchAsync("older", "jra", new(2026, 1, 1), new(2026, 1, 1),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await store.CreateOrResumeBackfillBatchAsync("newer", "jra", new(2026, 2, 1), new(2026, 2, 1),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var batches = await store.GetBackfillBatchesAsync();

        CollectionAssert.AreEqual(new[] { "newer", "older" }, batches.Select(x => x.BatchId).ToArray());
    }

    [TestMethod]
    public async Task Startup_ConcurrentStores_SerializeSchemaInitialization()
    {
        var stores = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(CreateStore)));

        Assert.HasCount(8, stores);
        await using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM collection_schema_history WHERE version = 6;";
        Assert.AreEqual(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task ResourceDetail_ReturnsStateLocationsRequestsTasksAndAttempts()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now,
            explicitUrl: new Uri("https://example.test/horse/H123"));
        await store.UpsertLocationAsync(Horse, HorseProfile, new Uri("https://example.test/horse/H123"),
            ResourceLocationSource.Manual, now);
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddMinutes(1),
            new(CollectionAttemptResult.Succeeded, RequestedUrl: new("https://example.test/horse/H123"),
                PageIdentification: "Horse:JRA:H123")));

        var detail = await store.GetResourceDetailAsync(Horse, HorseProfile);

        Assert.IsNotNull(detail);
        Assert.IsNotNull(detail.State);
        Assert.HasCount(1, detail.Locations);
        Assert.HasCount(1, detail.Requests);
        Assert.HasCount(1, detail.Tasks);
        Assert.HasCount(1, detail.Attempts);
        Assert.AreEqual(CollectionAttemptResult.Succeeded, detail.Attempts[0].Result);
        Assert.AreEqual("Horse:JRA:H123", detail.Attempts[0].PageIdentification);
    }

    [TestMethod]
    public async Task AttemptCorrelation_IsPersistedAndListsEveryTaskInExecutionBatch()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        var otherHorse = new ResourceKey(ResourceType.Horse, "JRA", "H456");
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var second = await store.RequestAsync(otherHorse, HorseProfile, 7, CollectionReason.Initial, now);
        var executionBatchId = Guid.NewGuid();
        var envelopeId = Guid.NewGuid();

        var firstLease = await store.AcquireAsync(first.TaskId, 1, now, TimeSpan.FromMinutes(5),
            new CollectionAttemptCorrelation(executionBatchId, envelopeId, "sqs-123", "lambda-456", 1, 2));
        var secondLease = await store.AcquireAsync(second.TaskId, 1, now.AddSeconds(1), TimeSpan.FromMinutes(5),
            new CollectionAttemptCorrelation(executionBatchId, envelopeId, "sqs-123", "lambda-456", 2, 2));
        Assert.IsNotNull(firstLease);
        Assert.IsNotNull(secondLease);
        await store.CompleteAttemptAsync(first.TaskId, firstLease.LeaseToken, now.AddSeconds(2),
            new(CollectionAttemptResult.Succeeded));
        await store.CompleteAttemptAsync(second.TaskId, secondLease.LeaseToken, now.AddSeconds(3),
            new(CollectionAttemptResult.ParseFailure, "Parser"));

        var detail = await store.GetResourceDetailAsync(Horse, HorseProfile);
        var attempt = detail!.Attempts.Single();
        Assert.AreEqual(executionBatchId, attempt.ExecutionBatchId);
        Assert.AreEqual(envelopeId, attempt.DispatchEnvelopeId);
        Assert.AreEqual("sqs-123", attempt.QueueMessageId);
        Assert.AreEqual("lambda-456", attempt.LambdaRequestId);
        Assert.AreEqual(1, attempt.BatchTaskOrdinal);
        Assert.AreEqual(2, attempt.BatchTaskCount);

        var batch = await store.GetExecutionBatchAsync(executionBatchId);
        Assert.IsNotNull(batch);
        Assert.AreEqual(2, batch.BatchTaskCount);
        Assert.HasCount(2, batch.Tasks);
        CollectionAssert.AreEqual(new[] { "H123", "H456" }, batch.Tasks.Select(x => x.Resource.Id).ToArray());
        CollectionAssert.AreEqual(new[] { CollectionAttemptResult.Succeeded, CollectionAttemptResult.ParseFailure },
            batch.Tasks.Select(x => x.Result).ToArray());
    }

    [TestMethod]
    public async Task StatePagingAndTaskErrorSearch_AreAppliedBeforePaging()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 60; index++)
        {
            var resource = new ResourceKey(ResourceType.Horse, "JRA", $"H{index:D3}");
            var receipt = await store.RequestAsync(resource, HorseProfile, 7, CollectionReason.Initial, now);
            if (index == 55)
            {
                var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
                Assert.IsNotNull(lease);
                await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(1),
                    new(CollectionAttemptResult.ParseFailure, "DistinctParserError", "grade parse failed"));
            }
        }

        var states = await store.SearchStatesAsync(new(Page: 2, PageSize: 25));
        var errors = await store.SearchTasksAsync(new(ErrorSearch: "DistinctParserError", Page: 1, PageSize: 1));

        Assert.AreEqual(60, states.TotalCount);
        Assert.HasCount(25, states.Items);
        Assert.AreEqual(1, errors.TotalCount);
        Assert.AreEqual("H055", errors.Items[0].Resource.Id);
    }

    private async Task<CollectionPlatformStore> CreateStoreAsync()
    {
        var store = CreateStore();
        await store.RegisterDefinitionAsync(HorseProfile, "Horse profile", ResourceType.Horse, 7,
            "Initial profile extractor", false);
        return store;
    }

    [TestMethod]
    public async Task Readiness_CountsOnlyActiveRequestsForRequestedRace()
    {
        var store = CreateStore();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse", ResourceType.Horse, 1, "Initial", false);
        await store.RegisterDefinitionAsync(new("jockey-profile"), "Jockey", ResourceType.Jockey, 1, "Initial", false);
        await store.RegisterDefinitionAsync(new("trainer-profile"), "Trainer", ResourceType.Trainer, 1, "Initial", false);
        var now = DateTimeOffset.UtcNow;
        var attributes = new Dictionary<string, string> { ["requestedByRaceId"] = "race-1" };
        var horse = await store.RequestAsync(new(ResourceType.Horse, "JRA", "h1"), new("horse-profile"),
            1, CollectionReason.Discovery, now, attributes: attributes);
        await store.RequestAsync(new(ResourceType.Jockey, "JRA", "j1"), new("jockey-profile"),
            1, CollectionReason.Discovery, now, attributes: attributes);
        await store.RequestAsync(new(ResourceType.Trainer, "JRA", "t2"), new("trainer-profile"),
            1, CollectionReason.Discovery, now, attributes: new Dictionary<string, string> { ["requestedByRaceId"] = "race-2" });

        var before = await store.GetReadinessAsync("race-1");
        Assert.AreEqual(1, before.PendingHorseRequests);
        Assert.AreEqual(1, before.PendingJockeyRequests);
        Assert.AreEqual(0, before.PendingTrainerRequests);
        Assert.AreEqual(2, before.TotalPendingRequests);

        await CompleteAsync(store, horse, now);
        var after = await store.GetReadinessAsync("race-1");
        Assert.AreEqual(0, after.PendingHorseRequests);
        Assert.AreEqual(1, after.TotalPendingRequests);
    }

    [TestMethod]
    public async Task ResourceDetail_HistoriesArePagedBeforeReturning()
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("horse-profile");
        var resource = new ResourceKey(ResourceType.Horse, "JRA", "history-horse");
        await store.RegisterDefinitionAsync(definition, "Horse", ResourceType.Horse, 1, "Initial", false);
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        for (var index = 0; index < 30; index++)
            await store.RequestAsync(resource, definition, 1, CollectionReason.ManualRefresh, now.AddSeconds(index));

        var secondPage = await store.GetResourceDetailPagedAsync(resource, definition, 2, 1, 1, 25);

        Assert.IsNotNull(secondPage);
        Assert.AreEqual(30, secondPage.RequestTotal);
        Assert.HasCount(5, secondPage.Requests);
        Assert.AreEqual(2, secondPage.HistoryPage);
        Assert.AreEqual(25, secondPage.HistoryPageSize);
        Assert.IsNotNull(secondPage.LatestTask);
        Assert.AreEqual(1, secondPage.EffectiveTaskHistoryPage);
        Assert.AreEqual(1, secondPage.EffectiveAttemptHistoryPage);
    }

    [TestMethod]
    public async Task ResourceDetail_PagesEachHistoryIndependently_AndAlwaysReturnsLatestTask()
    {
        var store = CreateStore();
        var definition = new CollectionDefinitionId("horse-profile");
        var resource = new ResourceKey(ResourceType.Horse, "JRA", "independent-history-horse");
        await store.RegisterDefinitionAsync(definition, "Horse", ResourceType.Horse, 1, "Initial", false);
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var receipts = new List<CollectionRequestReceipt>();
        for (var index = 0; index < 3; index++)
        {
            var requestedAt = now.AddMinutes(index * 2);
            var receipt = await store.RequestAsync(resource, definition, 1,
                CollectionReason.ManualRefresh, requestedAt);
            receipts.Add(receipt);
            await CompleteAsync(store, receipt, requestedAt);
        }

        var detail = await store.GetResourceDetailPagedAsync(resource, definition,
            requestHistoryPage: 2, taskHistoryPage: 3, attemptHistoryPage: 2, historyPageSize: 1);

        Assert.IsNotNull(detail);
        Assert.AreEqual(3, detail.RequestTotal);
        Assert.AreEqual(3, detail.TaskTotal);
        Assert.AreEqual(3, detail.AttemptTotal);
        Assert.HasCount(1, detail.Requests);
        Assert.HasCount(1, detail.Tasks);
        Assert.HasCount(1, detail.Attempts);
        Assert.AreEqual(receipts[0].TaskId, detail.Tasks[0].TaskId);
        Assert.AreEqual(receipts[1].TaskId, detail.Attempts[0].TaskId);
        Assert.AreEqual(receipts[2].TaskId, detail.LatestTask?.TaskId);
        Assert.AreEqual(2, detail.RequestHistoryPage);
        Assert.AreEqual(3, detail.EffectiveTaskHistoryPage);
        Assert.AreEqual(2, detail.EffectiveAttemptHistoryPage);
    }

    [TestMethod]
    public async Task ResourceDetail_NormalizesInvalidPaging_AndExtremePageReturnsEmpty()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var receipt = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now);
        await CompleteAsync(store, receipt, now);

        var normalized = await store.GetResourceDetailPagedAsync(Horse, HorseProfile,
            requestHistoryPage: 0, taskHistoryPage: -4, attemptHistoryPage: 0, historyPageSize: 0);
        var extreme = await store.GetResourceDetailPagedAsync(Horse, HorseProfile,
            requestHistoryPage: int.MaxValue, taskHistoryPage: int.MaxValue,
            attemptHistoryPage: int.MaxValue, historyPageSize: int.MaxValue);

        Assert.IsNotNull(normalized);
        Assert.AreEqual(1, normalized.RequestHistoryPage);
        Assert.AreEqual(1, normalized.EffectiveTaskHistoryPage);
        Assert.AreEqual(1, normalized.EffectiveAttemptHistoryPage);
        Assert.AreEqual(1, normalized.HistoryPageSize);
        Assert.HasCount(1, normalized.Requests);
        Assert.IsNotNull(extreme);
        Assert.AreEqual(100, extreme.HistoryPageSize);
        Assert.IsEmpty(extreme.Requests);
        Assert.IsEmpty(extreme.Tasks);
        Assert.IsEmpty(extreme.Attempts);
        Assert.AreEqual(receipt.TaskId, extreme.LatestTask?.TaskId);
    }

    [TestMethod]
    public async Task ResourceDetail_SameTimestampOrderingIsStableAcrossPages()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 5; index++)
            await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh, now);

        var first = await store.GetResourceDetailPagedAsync(Horse, HorseProfile, 1, 1, 1, 2);
        var second = await store.GetResourceDetailPagedAsync(Horse, HorseProfile, 2, 2, 2, 2);
        var repeated = await store.GetResourceDetailPagedAsync(Horse, HorseProfile, 1, 1, 1, 2);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.IsNotNull(repeated);
        CollectionAssert.AreEqual(first.Requests.Select(x => x.RequestId).ToArray(),
            repeated.Requests.Select(x => x.RequestId).ToArray());
        Assert.IsEmpty(first.Requests.Select(x => x.RequestId).Intersect(second.Requests.Select(x => x.RequestId)));
        Assert.AreEqual(first.LatestTask?.TaskId, second.LatestTask?.TaskId);
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
    [TestMethod]
    public async Task FailureResolution_IsIndependentFromPublishing_AndTracksRecoveryLifecycle()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var failed = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var failedLease = await store.AcquireAsync(failed.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(failedLease);
        await store.CompleteAttemptAsync(failed.TaskId, failedLease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "Broken"));
        var open = (await store.GetActionableFailureNotificationsAsync(now.AddMinutes(1), 10)).Single();

        await store.MarkFailureNotificationPublishedAsync(open.NotificationId, now.AddMinutes(1));
        Assert.IsEmpty(await store.GetUnpublishedFailureNotificationsAsync(now.AddMinutes(2), 10));
        Assert.HasCount(1, await store.GetActionableFailureNotificationsAsync(now.AddMinutes(2), 10));
        Assert.AreEqual(1, (await store.SearchTasksAsync(new(
            Statuses: [CollectionTaskStatus.Failed, CollectionTaskStatus.DeadLetter], ActionableOnly: true))).TotalCount);
        Assert.AreEqual(1, (await store.GetTaskViewCountsAsync()).Counts["attention"]);

        var recovery = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.ManualRefresh,
            now.AddMinutes(2));
        Assert.IsEmpty(await store.GetActionableFailureNotificationsAsync(now.AddMinutes(2), 10));
        Assert.AreEqual(0, (await store.SearchTasksAsync(new(
            Statuses: [CollectionTaskStatus.Failed, CollectionTaskStatus.DeadLetter], ActionableOnly: true))).TotalCount);
        Assert.AreEqual(1, (await store.SearchTasksAsync(new(
            Statuses: [CollectionTaskStatus.Failed, CollectionTaskStatus.DeadLetter]))).TotalCount,
            "The original failed task remains available as history.");
        Assert.AreEqual(0, (await store.GetTaskViewCountsAsync()).Counts["attention"]);
        var during = await store.GetResourceDetailAsync(Horse, HorseProfile);
        var duringFailure = during!.Failures!.Single();
        Assert.AreEqual(CollectionFailureResolutionStatus.RecoveryInProgress,
            duringFailure.ResolutionStatus);
        Assert.AreEqual(recovery.TaskId, duringFailure.RecoveryTaskId);

        var lease = await store.AcquireAsync(recovery.TaskId, 1, now.AddMinutes(2), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        await store.CompleteAttemptAsync(recovery.TaskId, lease.LeaseToken, now.AddMinutes(3),
            new(CollectionAttemptResult.Succeeded));
        var resolved = await store.GetResourceDetailAsync(Horse, HorseProfile);
        var resolvedFailure = resolved!.Failures!.Single();
        Assert.AreEqual(CollectionFailureResolutionStatus.Resolved, resolvedFailure.ResolutionStatus);
        Assert.IsNotNull(resolvedFailure.ResolvedAt);
    }

    [TestMethod]
    public async Task FailedRecovery_SupersedesOldFailure_AndLeavesOnlyLatestActionable()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var first = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var firstLease = await store.AcquireAsync(first.TaskId, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(first.TaskId, firstLease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "Old"));
        var recovery = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery, now.AddMinutes(1));
        var recoveryLease = await store.AcquireAsync(recovery.TaskId, 1, now.AddMinutes(1), TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(recovery.TaskId, recoveryLease!.LeaseToken, now.AddMinutes(2),
            new(CollectionAttemptResult.PermanentFailure, "New"));

        var actionable = (await store.GetActionableFailureNotificationsAsync(now.AddMinutes(3), 10)).Single();
        Assert.AreEqual("New", actionable.ErrorCode);
        var history = (await store.GetResourceDetailAsync(Horse, HorseProfile))!.Failures!;
        Assert.HasCount(2, history);
        Assert.AreEqual(CollectionFailureResolutionStatus.Superseded,
            history.Single(x => x.ErrorCode == "Old").ResolutionStatus);
    }

    [TestMethod]
    public async Task CancelledRecovery_ReopensFailureAndRestoresFailedState()
    {
        var store = await CreateStoreAsync();
        var now = DateTimeOffset.UtcNow;
        var failed = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(failed.TaskId, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(failed.TaskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "Broken"));
        var recovery = await store.RequestAsync(Horse, HorseProfile, 7, CollectionReason.Recovery, now.AddMinutes(1));

        Assert.IsTrue(await store.CancelTaskAsync(recovery.TaskId, now.AddMinutes(2)));

        var actionable = (await store.GetActionableFailureNotificationsAsync(now.AddMinutes(3), 10)).Single();
        Assert.AreEqual(CollectionFailureResolutionStatus.Open, actionable.ResolutionStatus);
        Assert.IsNull(actionable.RecoveryTaskId);
        Assert.AreEqual(CollectionStateStatus.Failed, (await store.GetStateAsync(Horse, HorseProfile))!.Status);
    }
}
