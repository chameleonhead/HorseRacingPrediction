using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using System.Text.Json;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionPlatformOperationsServicesTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "collection-platform-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task DlqReconciler_AuditsWakeWithoutChangingTaskAndDeletesMessage()
    {
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "jra", "H1"), new("horse-profile"),
            1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var queue = new RecordingQueue(new CollectionPlatformDeadLetterMessage("receipt-1", JsonSerializer.Serialize(
            new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "lease"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var service = new CollectionPlatformDeadLetterReconciler(store, queue,
            Options.Create(new CollectionDeadLetterQueueReconcilerOptions()),
            NullLogger<CollectionPlatformDeadLetterReconciler>.Instance);

        Assert.AreEqual(1, await service.RunOnceAsync(CancellationToken.None));
        CollectionAssert.AreEqual(new[] { "receipt-1" }, queue.Deleted);
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync()).Single(x => x.TaskId == taskId).Status);
    }

    [TestMethod]
    public async Task DlqReconciler_RetainsMalformedMessageForOperatorInspection()
    {
        var store = await CreateStoreAsync();
        var queue = new RecordingQueue(new CollectionPlatformDeadLetterMessage("receipt-bad", "not-json"));
        var service = new CollectionPlatformDeadLetterReconciler(store, queue,
            Options.Create(new CollectionDeadLetterQueueReconcilerOptions()),
            NullLogger<CollectionPlatformDeadLetterReconciler>.Instance);

        Assert.AreEqual(0, await service.RunOnceAsync(CancellationToken.None));
        Assert.HasCount(0, queue.Deleted);
    }

    [TestMethod]
    public async Task DlqReconciler_ReportsCommittedPartialAuditAsErrorWithProgress()
    {
        var store = await CreateStoreAsync();
        var validBody = JsonSerializer.Serialize(new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "lease"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var queue = new RecordingQueue(
            new CollectionPlatformDeadLetterMessage("good", validBody),
            new CollectionPlatformDeadLetterMessage("bad", "not-json"));
        var runtime = CreateRuntimeRecorder();
        var service = new CollectionPlatformDeadLetterReconciler(store, queue,
            Options.Create(new CollectionDeadLetterQueueReconcilerOptions()),
            NullLogger<CollectionPlatformDeadLetterReconciler>.Instance, runtime);

        Assert.AreEqual(1, await service.RunOnceAsync(CancellationToken.None));

        var status = runtime.GetSnapshot().Runtime.Actions.Single(x =>
            x.Action == CollectionRuntimeAction.DeadLetterReconciliation);
        Assert.AreEqual(CollectionRuntimeState.Error, status.State);
        Assert.AreEqual(CollectionRuntimeReason.Error, status.Reason);
        Assert.AreEqual(2, status.InspectedCount);
        Assert.AreEqual(1, status.CompletedCount);
        Assert.IsNotNull(status.LastProgressAtUtc);
        Assert.AreEqual(1, status.ConsecutiveErrors);
        CollectionAssert.AreEqual(new[] { "good" }, queue.Deleted);
    }

    [TestMethod]
    public void StalledSweep_RequiresThreeStableOverduePeriodsAndExcludesRepairHeldWork()
    {
        var clock = new ManualStatusClock(DateTimeOffset.Parse("2026-10-08T03:00:00Z"));
        var oldestDue = clock.GetUtcNow().AddMinutes(-1);

        Assert.IsFalse(CollectionScheduleService.ShouldReportStalledSweep(clock.GetUtcNow(), oldestDue,
            stableNoProgressPeriods: 1, hasEligibleCandidate: true, hasRepairHeldCandidate: false));
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.IsFalse(CollectionScheduleService.ShouldReportStalledSweep(clock.GetUtcNow(), oldestDue,
            stableNoProgressPeriods: 2, hasEligibleCandidate: true, hasRepairHeldCandidate: false));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.IsTrue(CollectionScheduleService.ShouldReportStalledSweep(clock.GetUtcNow(), oldestDue,
            stableNoProgressPeriods: 3, hasEligibleCandidate: true, hasRepairHeldCandidate: false));
        Assert.IsFalse(CollectionScheduleService.ShouldReportStalledSweep(clock.GetUtcNow(), oldestDue,
            stableNoProgressPeriods: 3, hasEligibleCandidate: false, hasRepairHeldCandidate: false));
        Assert.IsFalse(CollectionScheduleService.ShouldReportStalledSweep(clock.GetUtcNow(), oldestDue,
            stableNoProgressPeriods: 3, hasEligibleCandidate: true, hasRepairHeldCandidate: true));
    }

    [TestMethod]
    public async Task DlqReconciler_RetainsLegacyTaskNotificationWithoutChangingTask()
    {
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "jra", "H1"), new("horse-profile"),
            1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var notification = new CollectionTaskNotification(taskId, 1);
        var queue = new RecordingQueue(new CollectionPlatformDeadLetterMessage("legacy-v1",
            JsonSerializer.Serialize(notification, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var service = CreateReconciler(store, queue);

        Assert.AreEqual(0, await service.RunOnceAsync(CancellationToken.None));
        Assert.HasCount(0, queue.Deleted);
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync()).Single(x => x.TaskId == taskId).Status);
        Assert.HasCount(0, await store.GetActionableFailureNotificationsAsync(
            DateTimeOffset.UtcNow.AddMinutes(1), 10));
    }

    [TestMethod]
    public async Task DlqReconciler_AuditsMultipleWakeSignals()
    {
        var store = await CreateStoreAsync();
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var queue = new RecordingQueue(
            new CollectionPlatformDeadLetterMessage("wake-1", JsonSerializer.Serialize(
                new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "lease-1"), jsonOptions)),
            new CollectionPlatformDeadLetterMessage("wake-2", JsonSerializer.Serialize(
                new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "lease-2"), jsonOptions)));
        var service = CreateReconciler(store, queue);

        Assert.AreEqual(2, await service.RunOnceAsync(CancellationToken.None));
        CollectionAssert.AreEquivalent(new[] { "wake-1", "wake-2" }, queue.Deleted);
    }

    [TestMethod]
    public async Task DlqReconciler_DeletesRepeatedWakeWithoutCreatingTaskFailure()
    {
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "jra", "H1"), new("horse-profile"),
            1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var body = JsonSerializer.Serialize(new CollectionWakeSignal(Guid.NewGuid(), Guid.NewGuid(), "lease"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.AreEqual(1, await CreateReconciler(store,
            new RecordingQueue(new CollectionPlatformDeadLetterMessage("first", body)))
            .RunOnceAsync(CancellationToken.None));
        var repeatedQueue = new RecordingQueue(new CollectionPlatformDeadLetterMessage("repeated", body));
        Assert.AreEqual(1, await CreateReconciler(store, repeatedQueue).RunOnceAsync(CancellationToken.None));

        CollectionAssert.AreEqual(new[] { "repeated" }, repeatedQueue.Deleted);
        Assert.HasCount(0, await store.GetActionableFailureNotificationsAsync(
            DateTimeOffset.UtcNow.AddMinutes(1), 10));
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync()).Single(x => x.TaskId == taskId).Status);
    }

    [TestMethod]
    public async Task DlqReconciler_DiscardsLegacyOrInvalidNotificationWithoutChangingTasks()
    {
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "jra", "H1"), new("horse-profile"),
            1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var queue = new RecordingQueue(
            new CollectionPlatformDeadLetterMessage("legacy", $$"""{"taskId":"{{taskId}}","dispatchGeneration":1}"""),
            new CollectionPlatformDeadLetterMessage("invalid", JsonSerializer.Serialize(
                CreateEnvelope(Guid.Empty, 0), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var service = new CollectionPlatformDeadLetterReconciler(store, queue,
            Options.Create(new CollectionDeadLetterQueueReconcilerOptions()),
            NullLogger<CollectionPlatformDeadLetterReconciler>.Instance);

        Assert.AreEqual(0, await service.RunOnceAsync(CancellationToken.None));
        Assert.HasCount(0, queue.Deleted);
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync()).Single(x => x.TaskId == taskId).Status);
    }

    [TestMethod]
    public async Task DlqReconciler_RetainsLegacyNotificationWithUnknownOrAmbiguousShape()
    {
        var store = await CreateStoreAsync();
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "jra", "H1"), new("horse-profile"),
            1, CollectionReason.Initial, DateTimeOffset.UtcNow);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var queue = new RecordingQueue(
            new CollectionPlatformDeadLetterMessage("unknown-version",
                $$"""{"taskId":"{{taskId}}","dispatchGeneration":1,"contractVersion":2}"""),
            new CollectionPlatformDeadLetterMessage("ambiguous",
                $$"""{"taskId":"{{taskId}}","dispatchGeneration":1,"contractVersion":1,"extra":true}"""));

        Assert.AreEqual(0, await CreateReconciler(store, queue).RunOnceAsync(CancellationToken.None));
        Assert.HasCount(0, queue.Deleted);
        Assert.AreEqual(CollectionTaskStatus.Ready,
            (await store.GetTasksAsync()).Single(x => x.TaskId == taskId).Status);
    }

    [TestMethod]
    public async Task Watchdog_WhenPausedReclaimsExpiredLeaseWithoutRunningDispatchRecovery()
    {
        var store = await CreateStoreAsync();
        var now = HorseRacingPrediction.Contracts.Common.Time.JstTime.Now().AddMinutes(-10);
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "paused-watchdog"),
            new("horse-profile"), 1, CollectionReason.Initial, now);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("Expected an actionable task.");
        Assert.IsNotNull(await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(1)));
        await store.SetPausedAsync(true, "test pause", now);
        var watchdog = new CollectionPlatformWatchdogService(store,
            Options.Create(new CollectionJobWatchdogOptions()),
            NullLogger<CollectionPlatformWatchdogService>.Instance);

        var result = await watchdog.RunOnceAsync(CancellationToken.None);

        Assert.AreEqual(1, result.ReclaimedLeases);
        Assert.AreEqual(0, result.RedispatchedTasks);
        Assert.AreEqual(0, result.DeadLetteredTasks);
        Assert.IsTrue((await store.GetPipelineStateAsync()).IsPaused);
    }

    [TestMethod]
    public async Task WatchdogHostedLoop_UsesInjectedTimeForFirstConfiguredPeriodAfterExpiry()
    {
        var store = await CreateStoreAsync();
        var now = HorseRacingPrediction.Contracts.Common.Time.JstTime.Now();
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "watchdog-clock"),
            new("horse-profile"), 1, CollectionReason.Initial, now);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("Expected an actionable task.");
        Assert.IsNotNull(await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(2)));
        var clock = new TestTimeProvider(now.ToUniversalTime());
        var watchdog = new CollectionPlatformWatchdogService(store,
            Options.Create(new CollectionJobWatchdogOptions { IntervalMinutes = 2 }),
            NullLogger<CollectionPlatformWatchdogService>.Instance, clock);

        Assert.AreEqual(0, (await watchdog.RunOnceAsync(CancellationToken.None)).ReclaimedLeases);
        await watchdog.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await clock.WaitForTimerCountAsync(1);
            clock.Advance(TimeSpan.FromMinutes(2));
            await clock.WaitForTimerCountAsync(2);
        }
        finally
        {
            await watchdog.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }

        var attempts = await store.GetAttemptsAsync(taskId);
        Assert.IsTrue(attempts.Any(x => x.ErrorCode == "LeaseExpired"),
            "The hosted Watchdog should reclaim at its first configured period after expiry.");
    }

    [TestMethod]
    public async Task WatchdogWriterContention_UsesIndependentSqliteLockAndRetriesRecoveryExactlyOnce()
    {
        var now = HorseRacingPrediction.Contracts.Common.Time.JstTime.Now();
        var connectionString = $"Data Source={Path.Combine(_directory, "watchdog-writer-contention.db")};Pooling=False;Default Timeout=1";
        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>().UseSqlite(connectionString).Options;
        var store = new CollectionPlatformStore(options);
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", CollectionResourceType.Horse,
            1, "Initial", false);
        var startedAt = now.AddMinutes(-10);
        var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "watchdog-writer-lock"),
            new("horse-profile"), 1, CollectionReason.Initial, startedAt);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("Expected an actionable task.");
        Assert.IsNotNull(await store.AcquireAsync(taskId, 1, startedAt, TimeSpan.FromMinutes(1)));

        var watchdog = new CollectionPlatformWatchdogService(store,
            Options.Create(new CollectionJobWatchdogOptions()),
            NullLogger<CollectionPlatformWatchdogService>.Instance, new TestTimeProvider(now.ToUniversalTime()));
        await using var independentWriter = new SqliteConnection(connectionString);
        await independentWriter.OpenAsync();
        await using var blockingTransaction = independentWriter.BeginTransaction(deferred: false);

        try
        {
            Assert.IsFalse((await store.GetPipelineStateAsync().WaitAsync(TimeSpan.FromSeconds(5))).IsPaused,
                "Deferred reads should remain available while the independent connection holds its writer lock.");
            var lockFailure = await Assert.ThrowsAsync<SqliteException>(
                () => watchdog.RunOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(5, lockFailure.SqliteErrorCode,
                "The Store operation must be fenced by SQLite from a second connection, not by its in-process gate.");
        }
        finally
        {
            await blockingTransaction.RollbackAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }

        var recovered = await watchdog.RunOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, recovered.ReclaimedLeases);
        var attempts = await store.GetAttemptsAsync(taskId);
        Assert.IsTrue(attempts.Any(x => x.ErrorCode == "LeaseExpired"));

        var repeated = await watchdog.RunOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, repeated.ReclaimedLeases,
            "Retry after releasing the raw writer commits the expired lease recovery only once.");
        Assert.AreEqual(1, (await store.GetTasksAsync()).Count(x => x.TaskId == taskId));
    }

    [TestMethod]
    public async Task WatchdogRecoversExpiredLeaseWhileScheduleProducerIsBlockedInPolicy()
    {
        var store = await CreateStoreAsync();
        await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
            1, "Initial", false);
        var now = HorseRacingPrediction.Contracts.Common.Time.JstTime.Now();
        var leased = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "watchdog-independent"),
            new("horse-profile"), 1, CollectionReason.Initial, now.AddMinutes(-10));
        Assert.IsNotNull(await store.AcquireAsync(leased.TaskId!.Value, 1, now.AddMinutes(-10), TimeSpan.FromMinutes(1)));

        var dueResource = new ResourceKey(CollectionResourceType.Horse, "JRA", "blocked-schedule-policy");
        var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False").Options;
        await using (var db = new CollectionPlatformDbContext(dbOptions))
        {
            var resource = new CollectionResourceEntity
            {
                Type = dueResource.Type,
                Provider = dueResource.Provider,
                ResourceId = dueResource.Id,
                AttributesJson = "{}",
                CreatedAt = now,
            };
            db.Resources.Add(resource);
            await db.SaveChangesAsync();
            db.States.Add(new CollectionStateEntity
            {
                ResourcePk = resource.ResourcePk,
                DefinitionId = "horse-profile",
                AppliedRevision = 1,
                RequiredRevision = 1,
                LastCollectedAt = now.AddDays(-1),
                NextCollectionAt = now.AddMinutes(-1),
                Status = CollectionStateStatus.RefreshDue,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        using var enteredPolicy = new ManualResetEventSlim();
        using var releasePolicy = new ManualResetEventSlim();
        var policy = new BlockingSchedulePolicy(enteredPolicy, releasePolicy);
        var schedule = new CollectionScheduleService(store, [policy], NullLogger<CollectionScheduleService>.Instance);
        var planning = new CollectionPlanningScheduler(store);
        var clock = new TestTimeProvider(now.ToUniversalTime());
        var coordinator = new CollectionMaintenanceCoordinator(store, schedule, planning, clock,
            NullLogger<CollectionMaintenanceCoordinator>.Instance);
        var producer = Task.Run(() => coordinator.RunCycleAsync(CancellationToken.None));
        try
        {
            Assert.IsTrue(enteredPolicy.Wait(TimeSpan.FromSeconds(5)), "The schedule producer did not enter its blocking policy.");
            var watchdog = new CollectionPlatformWatchdogService(store,
                Options.Create(new CollectionJobWatchdogOptions()),
                NullLogger<CollectionPlatformWatchdogService>.Instance);

            var recovery = await watchdog.RunOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(1, recovery.ReclaimedLeases);
        }
        finally
        {
            releasePolicy.Set();
            await producer.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public async Task ScheduleService_RunOnceSkipsActiveAndAtomicallyDeduplicatesArrivalAfterSnapshot()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9));
        var activeResource = new ResourceKey(CollectionResourceType.Horse, "JRA", "schedule-active");
        var scheduledResource = new ResourceKey(CollectionResourceType.Horse, "JRA", "schedule-new");
        var lateResource = new ResourceKey(CollectionResourceType.Horse, "JRA", "schedule-late");
        var activeReceipt = await store.RequestAsync(activeResource, new("horse-profile"), 1,
            CollectionReason.Initial, now.AddMinutes(-3));

        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False").Options;
        await using (var db = new CollectionPlatformDbContext(options))
        {
            var resources = new[] { scheduledResource, lateResource }
                .Select(resource => new CollectionResourceEntity
                {
                    Type = resource.Type,
                    Provider = resource.Provider,
                    ResourceId = resource.Id,
                    AttributesJson = "{}",
                    CreatedAt = now,
                }).ToArray();
            db.Resources.AddRange(resources);
            await db.SaveChangesAsync();

            var active = await db.Resources.SingleAsync(x => x.ResourceId == activeResource.Id);
            db.States.Single(x => x.ResourcePk == active.ResourcePk && x.DefinitionId == "horse-profile")
                .NextCollectionAt = now.AddSeconds(-3);
            foreach (var (resource, index) in resources.Select((resource, index) => (resource, index)))
            {
                db.States.Add(new CollectionStateEntity
                {
                    ResourcePk = resource.ResourcePk,
                    DefinitionId = "horse-profile",
                    AppliedRevision = 1,
                    RequiredRevision = 1,
                    LastCollectedAt = now.AddDays(-1),
                    NextCollectionAt = now.AddSeconds(-2 + index),
                    Status = CollectionStateStatus.RefreshDue,
                    UpdatedAt = now,
                });
            }
            await db.SaveChangesAsync();
        }

        CollectionRequestReceipt? lateArrival = null;
        var policy = new RecordingSchedulePolicy(state =>
        {
            if (state.Resource == lateResource && lateArrival is null)
                lateArrival = store.RequestAsync(lateResource, new("horse-profile"), 1,
                    CollectionReason.Initial, now).GetAwaiter().GetResult();
        });
        var service = new CollectionScheduleService(store, [policy],
            NullLogger<CollectionScheduleService>.Instance);

        await service.RunOnceAsync(now, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { scheduledResource.Id, lateResource.Id, lateResource.Id },
            policy.EvaluatedResourceIds);
        Assert.IsNotNull(activeReceipt.TaskId);
        Assert.IsNotNull(lateArrival?.TaskId);
        var tasks = await store.GetTasksAsync();
        Assert.HasCount(3, tasks);
        Assert.AreEqual(1, tasks.Count(x => x.Resource == activeResource));
        Assert.AreEqual(1, tasks.Count(x => x.Resource == scheduledResource));
        Assert.AreEqual(1, tasks.Count(x => x.Resource == lateResource));
        Assert.AreEqual(lateArrival!.TaskId, tasks.Single(x => x.Resource == lateResource).TaskId,
            "The transactional scheduler guard must preserve the task created after the candidate snapshot.");
    }

    [TestMethod]
    public async Task ScheduleService_RunOnceHonorsCancellationBeforeScheduling()
    {
        var store = await CreateStoreAsync();
        var policy = new RecordingSchedulePolicy();
        var service = new CollectionScheduleService(store, [policy],
            NullLogger<CollectionScheduleService>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RunOnceAsync(DateTimeOffset.UtcNow, cancellation.Token));

        Assert.IsEmpty(policy.EvaluatedResourceIds);
        Assert.IsEmpty(await store.GetTasksAsync());
    }

    [TestMethod]
    public async Task ScheduleService_AdvancesPastFiveHundredPolicySkipsToEligibleBoundaryRow()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9));
        var resources = Enumerable.Range(0, 501).Select(index => new CollectionResourceEntity
        {
            Type = CollectionResourceType.Horse,
            Provider = "JRA",
            ResourceId = $"sweep-{index:D3}",
            AttributesJson = "{}",
            CreatedAt = now,
        }).ToArray();
        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False")
            .Options;
        await using (var db = new CollectionPlatformDbContext(options))
        {
            db.Resources.AddRange(resources);
            await db.SaveChangesAsync();
            db.States.AddRange(resources.Select(resource => new CollectionStateEntity
            {
                ResourcePk = resource.ResourcePk,
                DefinitionId = "horse-profile",
                AppliedRevision = 1,
                RequiredRevision = 1,
                LastCollectedAt = now.AddDays(-1),
                NextCollectionAt = now.AddMinutes(-1),
                Status = CollectionStateStatus.RefreshDue,
                UpdatedAt = now,
            }));
            await db.SaveChangesAsync();
        }

        var policy = new SelectiveSchedulePolicy("sweep-500");
        var runtime = CreateRuntimeRecorder();
        var service = new CollectionScheduleService(store, [policy],
            NullLogger<CollectionScheduleService>.Instance, runtime);

        await service.RunOnceAsync(now, CancellationToken.None);
        Assert.AreEqual(500, policy.EvaluatedResourceIds.Count);
        Assert.IsEmpty(await store.GetTasksAsync());
        var skipped = runtime.GetSnapshot().Runtime.Actions.Single(x =>
            x.Action == CollectionRuntimeAction.RefreshPlanner);
        Assert.AreEqual(500, skipped.InspectedCount);
        Assert.AreEqual(0, skipped.CreatedCount);
        Assert.IsNull(skipped.LastProgressAtUtc);

        await service.RunOnceAsync(now.AddMinutes(1), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "sweep-500" }, policy.EvaluatedResourceIds.Skip(500).ToArray());
        Assert.AreEqual("sweep-500", (await store.GetTasksAsync()).Single().Resource.Id);
        var scheduled = runtime.GetSnapshot().Runtime.Actions.Single(x =>
            x.Action == CollectionRuntimeAction.RefreshPlanner);
        Assert.AreEqual(1, scheduled.CreatedCount);
        Assert.IsNotNull(scheduled.LastProgressAtUtc);
    }

    [TestMethod]
    public async Task ScheduleService_TraversesOneThousandOneHeldAndIneligibleCandidatesInThreeBoundedPages()
    {
        var store = await CreateStoreAsync();
        await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
            1, "initial", false);
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9));
        const int count = 1_001;
        const int heldCount = 10;
        var resourceIds = Enumerable.Range(0, count)
            .Select(_ => $"race-{Guid.NewGuid():D}").ToArray();
        var resources = resourceIds.Select(resourceId => new CollectionResourceEntity
        {
            Type = CollectionResourceType.Race,
            Provider = "JRA",
            ResourceId = resourceId,
            AttributesJson = "{}",
            CreatedAt = now,
        }).ToArray();
        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False").Options;
        await using (var db = new CollectionPlatformDbContext(options))
        {
            db.Resources.AddRange(resources);
            await db.SaveChangesAsync();
            db.States.AddRange(resources.Select(resource => new CollectionStateEntity
            {
                ResourcePk = resource.ResourcePk,
                DefinitionId = "race-discovery",
                AppliedRevision = 1,
                RequiredRevision = 1,
                LastCollectedAt = now.AddDays(-1),
                NextCollectionAt = now.AddMinutes(-1),
                Status = CollectionStateStatus.RefreshDue,
                UpdatedAt = now,
            }));
            await db.SaveChangesAsync();
        }

        for (var index = 0; index < heldCount; index++)
            await store.HoldRaceForRepairAsync(resourceIds[index], Guid.NewGuid().ToString("D"), 0,
                "bounded fairness fixture", now);

        var policy = new SelectiveSchedulePolicy(resourceIds.Take(heldCount).Append(resourceIds[^1]).ToArray());
        var service = new CollectionScheduleService(store, [policy],
            NullLogger<CollectionScheduleService>.Instance);

        await service.RunOnceAsync(now, CancellationToken.None);
        Assert.AreEqual(500, policy.EvaluatedResourceIds.Count,
            "One invocation inspects no more than the bounded 500-candidate page.");
        CollectionAssert.AreEqual(resourceIds.Take(500).ToArray(), policy.EvaluatedResourceIds);
        Assert.IsEmpty(await store.GetTasksAsync());

        await service.RunOnceAsync(now.AddMinutes(1), CancellationToken.None);
        Assert.AreEqual(1_000, policy.EvaluatedResourceIds.Count);
        CollectionAssert.AreEqual(resourceIds.Skip(500).Take(500).ToArray(),
            policy.EvaluatedResourceIds.Skip(500).ToArray());
        Assert.IsEmpty(await store.GetTasksAsync());

        await service.RunOnceAsync(now.AddMinutes(2), CancellationToken.None);
        Assert.AreEqual(count, policy.EvaluatedResourceIds.Count);
        Assert.AreEqual(resourceIds[^1], policy.EvaluatedResourceIds[^1]);
        Assert.AreEqual(count, policy.EvaluatedResourceIds.Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(resourceIds[^1], (await store.GetTasksAsync()).Single().Resource.Id);
    }

    [TestMethod]
    public async Task ScheduleService_RevisitsRetimedCandidateBehindCursorAfterWrapAndIgnoresDeletedRow()
    {
        var store = await CreateStoreAsync();
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9));
        var resourceIds = Enumerable.Range(0, 501).Select(index => $"wrap-{index:D3}").ToArray();
        var resources = resourceIds.Select(resourceId => new CollectionResourceEntity
        {
            Type = CollectionResourceType.Horse,
            Provider = "JRA",
            ResourceId = resourceId,
            AttributesJson = "{}",
            CreatedAt = now,
        }).ToArray();
        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "collection-platform.db")};Pooling=False").Options;
        await using (var db = new CollectionPlatformDbContext(options))
        {
            db.Resources.AddRange(resources);
            await db.SaveChangesAsync();
            db.States.AddRange(resources.Select(resource => new CollectionStateEntity
            {
                ResourcePk = resource.ResourcePk,
                DefinitionId = "horse-profile",
                AppliedRevision = 1,
                RequiredRevision = 1,
                LastCollectedAt = now.AddDays(-1),
                NextCollectionAt = now.AddMinutes(-1),
                Status = CollectionStateStatus.RefreshDue,
                UpdatedAt = now,
            }));
            await db.SaveChangesAsync();
        }

        var policy = new SwitchableSchedulePolicy(resourceIds[10]);
        var service = new CollectionScheduleService(store, [policy],
            NullLogger<CollectionScheduleService>.Instance);
        await service.RunOnceAsync(now, CancellationToken.None);
        Assert.AreEqual(500, policy.EvaluatedResourceIds.Count);

        await using (var db = new CollectionPlatformDbContext(options))
        {
            var target = await db.Resources.SingleAsync(x => x.ResourceId == resourceIds[10]);
            var targetState = await db.States.SingleAsync(x => x.ResourcePk == target.ResourcePk);
            targetState.NextCollectionAt = now.AddMinutes(-2);
            var deleted = await db.Resources.SingleAsync(x => x.ResourceId == resourceIds[20]);
            db.States.Remove(await db.States.SingleAsync(x => x.ResourcePk == deleted.ResourcePk));
            db.Resources.Remove(deleted);
            await db.SaveChangesAsync();
        }

        await service.RunOnceAsync(now.AddMinutes(1), CancellationToken.None);
        Assert.AreEqual(501, policy.EvaluatedResourceIds.Count,
            "The retimed row sorts strictly behind the already-advanced cursor and is not revisited early.");
        Assert.AreEqual(resourceIds[500], policy.EvaluatedResourceIds[^1]);

        policy.Eligible = true;
        await service.RunOnceAsync(now.AddMinutes(2), CancellationToken.None);

        Assert.AreEqual(2, policy.EvaluatedResourceIds.Count(id => id == resourceIds[10]),
            "After the exhausted page wraps, the retimed-behind-cursor row is considered again.");
        Assert.AreEqual(1, policy.EvaluatedResourceIds.Count(id => id == resourceIds[20]),
            "The removed row is absent from later pages and the wrapped sweep.");
        Assert.AreEqual(resourceIds[10], (await store.GetTasksAsync()).Single().Resource.Id);
    }

    private async Task<CollectionPlatformStore> CreateStoreAsync()
    {
        var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
        {
            StateDirectory = _directory
        }));
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", CollectionResourceType.Horse,
            1, "initial", false);
        return store;
    }

    private static CollectionRuntimeStatusRecorder CreateRuntimeRecorder() =>
        new(Enum.GetValues<CollectionRuntimeAction>().Select(action =>
            new CollectionRuntimeActionConfiguration(action, true,
                action == CollectionRuntimeAction.MetricDelivery ? null : TimeSpan.FromMinutes(1))));

    private static CollectionDispatchEnvelope CreateEnvelope(Guid taskId, long generation) => new(Guid.NewGuid(),
        new("JRA", new("horse-profile"), null, CollectionLane.Normal), [new(taskId, generation)]);

    private sealed class RecordingSchedulePolicy(Action<CollectionStateSnapshot>? onEvaluate = null)
        : ICollectionSchedulePolicy
    {
        public List<string> EvaluatedResourceIds { get; } = [];

        public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now)
        {
            EvaluatedResourceIds.Add(resource.Id);
            onEvaluate?.Invoke(state);
            return new(true, state.NextCollectionAt, CollectionPriority.Normal, CollectionLane.Normal, "test");
        }
    }

    private sealed class SelectiveSchedulePolicy(params string[] eligibleResourceIds) : ICollectionSchedulePolicy
    {
        private readonly HashSet<string> _eligibleResourceIds = eligibleResourceIds.ToHashSet(StringComparer.Ordinal);
        public List<string> EvaluatedResourceIds { get; } = [];

        public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now)
        {
            EvaluatedResourceIds.Add(resource.Id);
            var shouldCollect = _eligibleResourceIds.Contains(resource.Id);
            return new(shouldCollect, state.NextCollectionAt, CollectionPriority.Normal,
                CollectionLane.Normal, shouldCollect ? "eligible" : "policy-skip");
        }
    }

    private sealed class SwitchableSchedulePolicy(string eligibleResourceId) : ICollectionSchedulePolicy
    {
        public bool Eligible { get; set; }
        public List<string> EvaluatedResourceIds { get; } = [];

        public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now)
        {
            EvaluatedResourceIds.Add(resource.Id);
            var shouldCollect = Eligible && resource.Id == eligibleResourceId;
            return new(shouldCollect, state.NextCollectionAt, CollectionPriority.Normal,
                CollectionLane.Normal, shouldCollect ? "eligible" : "policy-skip");
        }
    }

    private sealed class BlockingSchedulePolicy(ManualResetEventSlim entered, ManualResetEventSlim release)
        : ICollectionSchedulePolicy
    {
        public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now)
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test policy release timed out.");
            return new(false, null, CollectionPriority.Normal, CollectionLane.Normal, "not due");
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly object _sync = new();
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp;
        private int _timerCount;
        private TaskCompletionSource _timerChanged = NewSignal();
        private readonly List<ManualTimer> _timers = [];

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync) return _utcNow;
        }

        public override long GetTimestamp()
        {
            lock (_sync) return _timestamp;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            TaskCompletionSource changed;
            lock (_sync)
            {
                _timerCount++;
                changed = _timerChanged;
                _timerChanged = NewSignal();
            }
            changed.TrySetResult();
            return timer;
        }

        internal void Advance(TimeSpan elapsed)
        {
            ManualTimer[] due;
            lock (_sync)
            {
                _utcNow += elapsed;
                _timestamp += ToTimestampTicks(elapsed);
                due = _timers.Where(timer => timer.TakeIfDue(_timestamp)).ToArray();
            }
            foreach (var timer in due) timer.Fire();
        }

        internal async Task WaitForTimerCountAsync(int count)
        {
            while (true)
            {
                Task changed;
                lock (_sync)
                {
                    if (_timerCount >= count) return;
                    changed = _timerChanged.Task;
                }
                await changed.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        private long ToTimestampTicks(TimeSpan elapsed) =>
            (long)(elapsed.TotalSeconds * TimestampFrequency);

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class ManualTimer(TestTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private long? _dueTimestamp;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._sync)
                {
                    if (_disposed) return false;
                    _dueTimestamp = dueTime == Timeout.InfiniteTimeSpan
                        ? null
                        : owner._timestamp + owner.ToTimestampTicks(dueTime);
                    if (!owner._timers.Contains(this)) owner._timers.Add(this);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner._sync)
                {
                    _disposed = true;
                    _dueTimestamp = null;
                    owner._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal bool TakeIfDue(long timestamp)
            {
                if (_disposed || _dueTimestamp is not { } due || timestamp < due) return false;
                _dueTimestamp = null;
                return true;
            }

            internal void Fire() => callback(state);
        }
    }

    private static CollectionPlatformDeadLetterReconciler CreateReconciler(CollectionPlatformStore store,
        ICollectionPlatformTaskQueue queue) => new(store, queue,
        Options.Create(new CollectionDeadLetterQueueReconcilerOptions()),
        NullLogger<CollectionPlatformDeadLetterReconciler>.Instance);

    private sealed class RecordingQueue(params CollectionPlatformDeadLetterMessage[] messages)
        : ICollectionPlatformTaskQueue
    {
        public List<string> Deleted { get; } = [];
        public Task<CollectionQueueSendReceipt> SendAsync(CollectionDispatchEnvelope envelope,
            CancellationToken cancellationToken)
            => Task.FromResult(new CollectionQueueSendReceipt(null));
        public Task<IReadOnlyList<CollectionPlatformDeadLetterMessage>> ReceiveDeadLetterMessagesAsync(
            int maxMessages, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<CollectionPlatformDeadLetterMessage>>(messages.Take(maxMessages).ToList());
        public Task DeleteDeadLetterMessageAsync(string receiptHandle, CancellationToken cancellationToken)
        {
            Deleted.Add(receiptHandle);
            return Task.CompletedTask;
        }
    }

    private sealed class ManualStatusClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now = _now.Add(elapsed);
    }
}
