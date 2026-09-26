using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class RaceRepairHoldTests
{
    private static readonly CollectionDefinitionId Detail = new("race-detail");
    private static readonly ResourceKey Target = new(ResourceType.Race, "JRA", "20260926:Nakayama:5");
    private static readonly string RaceId = DeterministicIdGenerator.TryBuildRaceIdFromResource(Target.Id)!;

    [TestMethod]
    public async Task HeldRequestsRetainIntent_AndReleaseLatestRevisionExactlyOnceWithoutResuming()
    {
        var (store, options) = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var old = await store.RequestAsync(Target, Detail, 2, CollectionReason.Initial, now);
        var outbox = (await store.GetPendingDispatchesAsync(now, 100)).Single();
        var hold = await store.HoldRaceForRepairAsync(RaceId, Guid.NewGuid().ToString(), 0, "review", now);
        Assert.IsTrue(hold.IsQuiescent);
        Assert.AreEqual(1, hold.ReadyTasks);
        Assert.IsNull(await store.AcquireAsync(old.TaskId!.Value, 1, now, TimeSpan.FromMinutes(1)));
        Assert.AreEqual(CollectionTaskAcquireStatus.RepairHeld, await store.ClassifyAcquireFailureAsync(old.TaskId.Value, 1));
        Assert.IsFalse(await store.TryReserveDispatchesAsync([outbox.OutboxId], "old", Guid.NewGuid(), now, TimeSpan.FromMinutes(1)));
        await store.RegisterDefinitionAsync(Detail, "detail", ResourceType.Race, 7, "future registered revision", false);
        for (var tick = 0; tick < 3; tick++)
        {
            var receipt = await store.RequestAsync(Target, Detail, 7, CollectionReason.ScheduledRefresh, now.AddSeconds(tick + 1),
                CollectionLane.Realtime, 90, batchId: "held-intent");
            Assert.IsTrue(receipt.DeferredByRepairHold);
            Assert.IsFalse(receipt.CreatedTask);
            Assert.AreEqual(old.TaskId, receipt.TaskId);
        }
        await store.RequestAsync(Target, Detail, 4, CollectionReason.ScheduledRefresh, now.AddSeconds(10),
            CollectionLane.Background, 10, batchId: "later-lower-priority");
        Assert.IsEmpty(await store.GetPendingDispatchesAsync(now.AddMinutes(1), 100));
        var restarted = new CollectionPlatformStore(Options.Create(options));
        Assert.IsTrue((await restarted.GetRaceRepairHoldAsync(RaceId))!.IsActive);
        await restarted.SetPausedAsync(true, "keep paused", now);
        var releaseId = Guid.NewGuid().ToString();
        var released = await restarted.ReleaseRaceRepairHoldAsync(RaceId, hold.OperationId, hold.Generation, releaseId, "verified", now.AddMinutes(1));
        Assert.IsFalse(released.IsActive);
        Assert.IsTrue((await restarted.GetPipelineStateAsync()).IsPaused);
        await restarted.ReleaseRaceRepairHoldAsync(RaceId, hold.OperationId, hold.Generation, releaseId, "verified", now.AddMinutes(2));
        var detail = (await restarted.GetResourceDetailAsync(Target, Detail))!;
        Assert.AreEqual(2, detail.TaskTotal);
        Assert.AreEqual(CollectionTaskStatus.Cancelled, detail.Tasks.Single(x => x.TaskId == old.TaskId).Status);
        var fresh = detail.Tasks.Single(x => x.Status == CollectionTaskStatus.Ready);
        Assert.AreEqual(7, fresh.RequestedRevision);
        Assert.AreEqual(CollectionLane.Realtime, fresh.Lane);
        Assert.AreEqual(90, fresh.Priority);
        await restarted.SetPausedAsync(false, null, now.AddMinutes(3));
        var lease = await restarted.AcquireAsync(fresh.TaskId, 1, now.AddMinutes(3), TimeSpan.FromMinutes(1));
        Assert.IsNotNull(lease);
        Assert.AreEqual(hold.Generation, lease.RaceHoldGeneration);
        Assert.IsNull(await restarted.AcquireAsync(old.TaskId.Value, 1, now.AddMinutes(3), TimeSpan.FromMinutes(1)));
    }

    [TestMethod]
    public async Task TasklessReceiptPersists_WhileOtherRaceAndDayDiscoveryRemainRunnable()
    {
        var (store, _) = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await store.RegisterDefinitionAsync(new("race-discovery"), "discovery", ResourceType.Race, 1, "test", false);
        await store.RequestAsync(new(ResourceType.Race, "JRA", "backfill:20260920"), new("race-discovery"), 1, CollectionReason.Initial, now);
        await store.RequestAsync(new(ResourceType.Race, "JRA", "discovery:2026092615"), new("race-discovery"), 1, CollectionReason.Initial, now);
        var hold = await store.HoldRaceForRepairAsync(RaceId, Guid.NewGuid().ToString(), 0, "review", now);
        Assert.IsTrue(hold.IsQuiescent);
        var deferred = await store.RequestAsync(Target, Detail, 4, CollectionReason.ManualRefresh, now, batchId: "no-task");
        Assert.IsNull(deferred.TaskId);
        Assert.IsTrue(deferred.DeferredByRepairHold);
        var duplicate = await store.RequestAsync(Target, Detail, 4, CollectionReason.ManualRefresh, now, batchId: "no-task");
        Assert.AreEqual(deferred.RequestId, duplicate.RequestId);
        var other = await store.RequestAsync(Target with { Id = "20260926:Nakayama:6" }, Detail, 4, CollectionReason.Initial, now);
        Assert.IsNotNull(other.TaskId);
        Assert.HasCount(3, await store.GetPendingDispatchesAsync(now, 100));
        Assert.IsNotNull(await store.AcquireAsync(other.TaskId.Value, 1, now, TimeSpan.FromMinutes(1)));
        await store.ReleaseRaceRepairHoldAsync(RaceId, hold.OperationId, hold.Generation, Guid.NewGuid().ToString(), "verified", now);
        var rebound = await store.RequestAsync(Target, Detail, 4, CollectionReason.ManualRefresh, now, batchId: "no-task");
        Assert.IsNotNull(rebound.TaskId);
        Assert.AreEqual(deferred.RequestId, rebound.RequestId);
    }

    [TestMethod]
    public async Task RunningLeaseMustDrain_AndCannotWriteAfterHoldOrRelease()
    {
        var (store, _) = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var receipt = await store.RequestAsync(Target, Detail, 2, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(receipt.TaskId!.Value, 1, now, TimeSpan.FromMinutes(2));
        Assert.IsNotNull(lease);
        var hold = await store.HoldRaceForRepairAsync(RaceId, Guid.NewGuid().ToString(), 0, "drain", now);
        Assert.IsFalse(hold.IsQuiescent);
        Assert.AreEqual(1, hold.RunningTasks);
        Assert.IsGreaterThan(0, hold.UnresolvedLeases);
        Assert.IsFalse(await store.IsValidActiveRaceLeaseAsync(lease.TaskId, lease.LeaseToken, RaceId));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.ReleaseRaceRepairHoldAsync(RaceId,
            hold.OperationId, hold.Generation, Guid.NewGuid().ToString(), "verified", now));
        var drained = await store.HoldRaceForRepairAsync(RaceId, hold.OperationId, 0, "drain", now.AddMinutes(3));
        Assert.IsTrue(drained.IsQuiescent);
        await store.ReleaseRaceRepairHoldAsync(RaceId, hold.OperationId, hold.Generation, Guid.NewGuid().ToString(), "verified", now.AddMinutes(3));
        Assert.IsFalse(await store.IsValidActiveRaceLeaseAsync(lease.TaskId, lease.LeaseToken, RaceId));
    }

    [TestMethod]
    public async Task MixedEnvelopeReplayExcludesHeldRace_ButLeaseMustFinishBeforeRepair()
    {
        var (store, _) = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var first = await store.RequestAsync(Target, Detail, 4, CollectionReason.Initial, now);
        var other = await store.RequestAsync(Target with { Id = "20260926:Nakayama:6" }, Detail, 4, CollectionReason.Initial, now);
        var rows = await store.GetPendingDispatchesAsync(now, 100);
        var envelopeId = Guid.NewGuid();
        Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync(rows.Select(x => x.OutboxId).ToArray(), "reserved",
            envelopeId, now, TimeSpan.FromMinutes(1), 2));
        var wake = new CollectionWakeSignal(Guid.NewGuid(), envelopeId, "reserved");
        var execution = await store.AcquireNextExecutionAsync(wake, "message", now, TimeSpan.FromSeconds(45));
        Assert.AreEqual(CollectionExecutionAcquireStatus.Acquired, execution.Status);
        var hold = await store.HoldRaceForRepairAsync(RaceId, Guid.NewGuid().ToString(), 0, "drain execution", now);
        Assert.IsFalse(hold.IsQuiescent);
        Assert.AreEqual(1, hold.UnresolvedLeases);
        var replay = await store.AcquireNextExecutionAsync(wake, "message", now, TimeSpan.FromSeconds(45));
        Assert.HasCount(1, replay.Envelope!.Tasks);
        Assert.AreEqual(other.TaskId, replay.Envelope.Tasks[0].TaskId);
        Assert.IsTrue(await store.StartExecutionAsync(execution.ExecutionBatchId!.Value, new(execution.LeaseToken!, 60), now));
        Assert.IsNull(await store.AcquireAsync(first.TaskId!.Value, 1, now, TimeSpan.FromMinutes(1)));
        Assert.IsNotNull(await store.AcquireAsync(other.TaskId!.Value, 1, now, TimeSpan.FromMinutes(1)));
        Assert.IsTrue(await store.CompleteExecutionAsync(execution.ExecutionBatchId.Value, execution.LeaseToken!, now));
        Assert.IsTrue((await store.GetRaceRepairHoldAsync(RaceId))!.IsQuiescent);
    }

    [TestMethod]
    public async Task HeldLegacyEnvelopeDoesNotConsumeUnrelatedDispatchCapacity()
    {
        var (store, _) = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await store.RequestAsync(Target, Detail, 4, CollectionReason.Initial, now);
        var old = (await store.GetPendingDispatchesAsync(now, 10)).Single();
        Assert.IsTrue(await store.TryReserveDispatchesAsync([old.OutboxId], "old", Guid.NewGuid(), now, TimeSpan.FromMinutes(1)));
        await store.HoldRaceForRepairAsync(RaceId, Guid.NewGuid().ToString(), 0, "review", now);
        await store.RequestAsync(Target with { Id = "20260926:Nakayama:6" }, Detail, 4, CollectionReason.Initial, now);
        var other = (await store.GetPendingDispatchesAsync(now, 10)).Single();
        Assert.IsTrue(await store.TryReserveDispatchesWithinCapacityAsync([other.OutboxId], "new", Guid.NewGuid(), now, TimeSpan.FromMinutes(1), 1));
    }

    [TestMethod]
    public async Task BatchTasklessBindingSurvivesReleaseAndTerminalReplay()
    {
        var (store, _) = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var hold = await store.HoldRaceForRepairAsync(RaceId, Guid.NewGuid().ToString(), 0, "review", now);
        CollectionRequestBatchItem[] items = [new("target", Target, Detail, 4, CollectionReason.Initial,
            CollectionLane.Normal, 50, null, new(2026, 9, 26), null)];
        var first = (await store.RequestManyAsync("held-batch", items, now)).Single();
        Assert.AreEqual("Held", first.Status);
        Assert.IsNull(first.Receipt!.TaskId);
        await store.ReleaseRaceRepairHoldAsync(RaceId, hold.OperationId, hold.Generation, Guid.NewGuid().ToString(), "verified", now);
        var replay = (await store.RequestManyAsync("held-batch", items, now)).Single();
        Assert.AreEqual(first.Receipt.RequestId, replay.Receipt!.RequestId);
        Assert.IsNotNull(replay.Receipt.TaskId);
        var task = await store.AcquireAsync(replay.Receipt.TaskId.Value, 1, now, TimeSpan.FromMinutes(1));
        Assert.IsNotNull(task);
        await store.CompleteAttemptAsync(task.TaskId, task.LeaseToken, now, new(CollectionAttemptResult.Succeeded));
        var terminalReplay = (await store.RequestManyAsync("held-batch", items, now)).Single();
        Assert.AreEqual(replay.Receipt.TaskId, terminalReplay.Receipt!.TaskId);
        Assert.AreEqual(1, (await store.GetResourceDetailAsync(Target, Detail))!.TaskTotal);
    }

    [TestMethod]
    public async Task SchemaSeventeenMigrationPreservesBindingsAndAllowsTasklessRequests()
    {
        var (store, options) = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        CollectionRequestBatchItem[] items = [new("old", Target, Detail, 4, CollectionReason.Initial,
            CollectionLane.Normal, 50, null, null, null)];
        var before = (await store.RequestManyAsync("before-migration", items, now)).Single().Receipt!;
        using (var connection = new SqliteConnection("Data Source=" + Path.Combine(options.StateDirectory, "collection-platform.db")))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                BEGIN IMMEDIATE;
                DELETE FROM collection_schema_history WHERE version = 18;
                DROP TABLE race_repair_holds;
                ALTER TABLE collection_tasks DROP COLUMN RaceHoldGeneration;
                CREATE TABLE v17_bindings (BatchItemId TEXT NOT NULL PRIMARY KEY, PayloadFingerprint TEXT NOT NULL, RequestId TEXT NOT NULL, TaskId TEXT NOT NULL);
                INSERT INTO v17_bindings SELECT * FROM collection_request_batch_bindings;
                DROP TABLE collection_request_batch_bindings;
                ALTER TABLE v17_bindings RENAME TO collection_request_batch_bindings;
                INSERT INTO collection_schema_history (version, applied_at) VALUES (17, '2026-09-26T00:00:00');
                COMMIT;
                """;
            await command.ExecuteNonQueryAsync();
        }
        var migrated = new CollectionPlatformStore(Options.Create(options));
        var replay = (await migrated.RequestManyAsync("before-migration", items, now)).Single().Receipt!;
        Assert.AreEqual(before, replay with { CreatedTask = before.CreatedTask });
        var otherRace = DeterministicIdGenerator.TryBuildRaceIdFromResource("20260926:Nakayama:6")!;
        await migrated.HoldRaceForRepairAsync(otherRace, Guid.NewGuid().ToString(), 0, "new", now);
        var deferred = (await migrated.RequestManyAsync("after-migration", [items[0] with { ItemKey = "new", Resource = Target with { Id = "20260926:Nakayama:6" } }], now)).Single();
        Assert.AreEqual("Held", deferred.Status);
        Assert.IsNull(deferred.Receipt!.TaskId);
        Assert.IsTrue((await new CollectionPlatformStore(Options.Create(options)).GetRaceRepairHoldAsync(otherRace))!.IsActive);
    }

    [TestMethod]
    public async Task FailedReleaseRollsBackHoldAndEveryMaterializedRequest()
    {
        var (store, options) = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var old = await store.RequestAsync(Target, Detail, 4, CollectionReason.Initial, now);
        await store.RegisterDefinitionAsync(new("race-odds"), "odds", ResourceType.RaceOdds, 1, "test", false);
        await store.RequestAsync(new(ResourceType.RaceOdds, "JRA", Target.Id), new("race-odds"), 1, CollectionReason.Initial, now);
        var hold = await store.HoldRaceForRepairAsync(RaceId, Guid.NewGuid().ToString(), 0, "review", now);
        using (var connection = new SqliteConnection("Data Source=" + Path.Combine(options.StateDirectory, "collection-platform.db")))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE collection_definitions SET Enabled=0 WHERE DefinitionId='race-odds'";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.ReleaseRaceRepairHoldAsync(RaceId,
            hold.OperationId, hold.Generation, Guid.NewGuid().ToString(), "verified", now));
        var restarted = new CollectionPlatformStore(Options.Create(options));
        Assert.IsTrue((await restarted.GetRaceRepairHoldAsync(RaceId))!.IsActive);
        var detail = (await restarted.GetResourceDetailAsync(Target, Detail))!;
        Assert.AreEqual(1, detail.TaskTotal);
        Assert.AreEqual(old.TaskId, detail.Tasks.Single().TaskId);
        Assert.AreEqual(CollectionTaskStatus.Ready, detail.Tasks.Single().Status);
    }

    [TestMethod]
    public async Task HeldWorkerCompletionIsIsolated_AndCancelCannotMaterializeNewRevision()
    {
        var (store, _) = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var old = await store.RequestAsync(Target, Detail, 2, CollectionReason.Initial, now);
        var lease = await store.AcquireAsync(old.TaskId!.Value, 1, now, TimeSpan.FromMinutes(1));
        Assert.IsNotNull(lease);
        var hold = await store.HoldRaceForRepairAsync(RaceId, Guid.NewGuid().ToString(), 0, "review", now);
        await store.RequestAsync(Target, Detail, 4, CollectionReason.DefinitionChanged, now);
        var completion = CollectionAttemptFailureClassifier.FromException(new HorseRacingPrediction.Contracts.CollectionRepairHeldException());
        Assert.AreEqual(CollectionFailureImpact.Isolated, completion.FailureImpact);
        Assert.IsTrue(await store.CompleteAttemptAsync(lease.TaskId, lease.LeaseToken, now, completion));
        Assert.IsFalse((await store.GetPipelineStateAsync()).IsPaused);
        Assert.IsTrue((await store.GetRaceRepairHoldAsync(RaceId))!.IsQuiescent);
        await store.CancelTaskAsync(lease.TaskId, now);
        Assert.AreEqual(1, (await store.GetResourceDetailAsync(Target, Detail))!.TaskTotal);
        Assert.IsEmpty(await store.GetPendingDispatchesAsync(now.AddHours(1), 100));
        await store.ReleaseRaceRepairHoldAsync(RaceId, hold.OperationId, hold.Generation, Guid.NewGuid().ToString(), "verified", now);
        Assert.AreEqual(2, (await store.GetResourceDetailAsync(Target, Detail))!.TaskTotal);
    }

    [TestMethod]
    public async Task ConflictingAliasAndUnknownDefinitionFailClosed()
    {
        var (store, _) = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await store.RequestAsync(Target, Detail, 4, CollectionReason.Initial, now,
            attributes: new Dictionary<string, string> { ["domainRaceId"] = "race-" + Guid.NewGuid() });
        var hold = await store.HoldRaceForRepairAsync(RaceId, Guid.NewGuid().ToString(), 0, "review", now);
        Assert.IsFalse(hold.IsQuiescent);
        Assert.IsTrue(hold.Blockers.Any(x => x.StartsWith("UnknownRaceAlias:")));
        Assert.IsEmpty(await store.GetPendingDispatchesAsync(now, 100));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.ReleaseRaceRepairHoldAsync(RaceId,
            hold.OperationId, hold.Generation, Guid.NewGuid().ToString(), "verified", now));
    }

    private static async Task<(CollectionPlatformStore Store, CollectionPlatformOptions Options)> CreateAsync()
    {
        var options = new CollectionPlatformOptions { StateDirectory = Path.Combine(Path.GetTempPath(), "hrp-hold-test", Guid.NewGuid().ToString("N")) };
        var store = new CollectionPlatformStore(Options.Create(options));
        await store.RegisterDefinitionAsync(Detail, "detail", ResourceType.Race, 2, "old revision", false);
        await store.RegisterDefinitionAsync(Detail, "detail", ResourceType.Race, 4, "confirmed numbers", false);
        return (store, options);
    }
}
