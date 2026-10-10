using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using HorseRacingPrediction.Contracts.Collection;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionMaintenanceCoordinatorTests
{
    [TestMethod]
    public void WatchdogCadence_FollowsBackgroundSchedulerTruthTable()
    {
        AssertCadence(true, true, 9, true, 1);
        AssertCadence(true, false, 9, true, 1);
        AssertCadence(false, true, 9, true, 9);
        AssertCadence(false, false, 9, false, 9);
    }

    private static void AssertCadence(bool backgroundEnabled, bool watchdogEnabled, int configuredMinutes,
        bool expectedEnabled, int expectedMinutes)
    {
        var actual = CollectionProducerCadencePolicy.GetWatchdogCadence(
            backgroundEnabled, watchdogEnabled, configuredMinutes);
        Assert.AreEqual(expectedEnabled, actual.Enabled);
        Assert.AreEqual(expectedMinutes, actual.IntervalMinutes);
    }

    [TestMethod]
    public void ClockJump_UsesWallClockVersusMonotonicElapsedAndDueStartsAfterCompletion()
    {
        var start = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);

        Assert.IsFalse(CollectionProducerCadencePolicy.IsClockJump(start, start.AddSeconds(30),
            TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1)));
        Assert.IsTrue(CollectionProducerCadencePolicy.IsClockJump(start, start.AddHours(1),
            TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1)));
        Assert.IsTrue(CollectionProducerCadencePolicy.IsClockJump(start, start.AddHours(-1),
            TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1)));
        Assert.AreEqual(start.AddMinutes(2), CollectionProducerCadencePolicy.NextDueAfterCompletion(
            start.AddMinutes(1), TimeSpan.FromMinutes(1)));
    }

    [TestMethod]
    public async Task PausedPipelineSuppressesDiscoveryProducer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-maintenance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = directory,
            }));
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
                1, "Initial", false);
            await store.SetPausedAsync(true, "test pause", DateTimeOffset.UtcNow);
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 8, 4, 42, 0, TimeSpan.Zero));
            var schedule = new CollectionScheduleService(store, [new NeverDuePolicy()],
                NullLogger<CollectionScheduleService>.Instance);
            var planning = new CollectionPlanningScheduler(store);
            var coordinator = new CollectionMaintenanceCoordinator(store, schedule, planning, clock,
                NullLogger<CollectionMaintenanceCoordinator>.Instance);

            await coordinator.RunCycleAsync(CancellationToken.None);

            Assert.IsEmpty(await store.GetTasksAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PausedPipelineRecordsPausedReasonsWithoutReportingProgress()
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-maintenance-runtime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = directory,
            }));
            var runtime = new CollectionRuntimeStatusRecorder(Enum.GetValues<CollectionRuntimeAction>().Select(action =>
                new CollectionRuntimeActionConfiguration(action, true,
                    action == CollectionRuntimeAction.MetricDelivery ? null : TimeSpan.FromMinutes(1))));
            await store.SetPausedAsync(true, "test pause", DateTimeOffset.UtcNow);
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 8, 4, 42, 0, TimeSpan.Zero));
            var schedule = new CollectionScheduleService(store, [new NeverDuePolicy()],
                NullLogger<CollectionScheduleService>.Instance, runtime);
            var planning = new CollectionPlanningScheduler(store, runtime);
            var coordinator = new CollectionMaintenanceCoordinator(store, schedule, planning, clock,
                NullLogger<CollectionMaintenanceCoordinator>.Instance, runtime);

            await coordinator.RunCycleAsync(CancellationToken.None);

            var statuses = runtime.GetSnapshot().Runtime.Actions.ToDictionary(x => x.Action);
            Assert.AreEqual(CollectionRuntimeReason.Paused, statuses[CollectionRuntimeAction.RefreshPlanner].Reason);
            Assert.AreEqual(CollectionRuntimeReason.Paused, statuses[CollectionRuntimeAction.DiscoveryPlanner].Reason);
            Assert.IsNull(statuses[CollectionRuntimeAction.RefreshPlanner].LastProgressAtUtc);
            Assert.IsNull(statuses[CollectionRuntimeAction.DiscoveryPlanner].LastProgressAtUtc);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FirstCycleRunsRefreshAndDiscoveryActions()
    {
        var (directory, store, now) = await CreateStoreWithDueResourcesAsync(1);
        try
        {
            var policy = new RecordingPolicy();
            var clock = new ManualTimeProvider(now.ToUniversalTime());
            var coordinator = CreateCoordinator(store, policy, clock);
            await Task.Run(() => coordinator.StartAsync(CancellationToken.None))
                .WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await clock.WaitForTimerCountAsync(1);

                Assert.HasCount(1, policy.EvaluatedResourceIds);
                Assert.AreEqual("discovery:2026100812", (await store.GetTasksAsync()).Single().Resource.Id);
            }
            finally
            {
                await coordinator.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DiscoveryStillRunsWhenRefreshActionFails()
    {
        var (directory, store, now) = await CreateStoreWithDueResourcesAsync(1);
        try
        {
            var coordinator = CreateCoordinator(store, new ThrowingPolicy(), new ManualTimeProvider(now.ToUniversalTime()));

            await coordinator.RunCycleAsync(CancellationToken.None);

            Assert.AreEqual("discovery:2026100812", (await store.GetTasksAsync()).Single().Resource.Id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PipelineStatusReadFailureMarksBothPlannersErrorAndCancellationDoesNotAddAnError()
    {
        var (directory, store, now) = await CreateStoreWithDueResourcesAsync(1);
        try
        {
            var clock = new ManualTimeProvider(now.ToUniversalTime());
            var runtime = new CollectionRuntimeStatusRecorder(Enum.GetValues<CollectionRuntimeAction>().Select(action =>
                new CollectionRuntimeActionConfiguration(action, true,
                    action == CollectionRuntimeAction.MetricDelivery ? null : TimeSpan.FromMinutes(1))));
            var coordinator = new CollectionMaintenanceCoordinator(store,
                new CollectionScheduleService(store, [new AlwaysDuePolicy()],
                    NullLogger<CollectionScheduleService>.Instance, runtime),
                new CollectionPlanningScheduler(store, runtime), clock,
                NullLogger<CollectionMaintenanceCoordinator>.Instance, runtime);

            await coordinator.RunCycleAsync(CancellationToken.None);
            var beforeFailure = runtime.GetSnapshot().Runtime.Actions
                .Where(x => x.Action is CollectionRuntimeAction.RefreshPlanner or CollectionRuntimeAction.DiscoveryPlanner)
                .ToDictionary(x => x.Action);
            Assert.IsNotNull(beforeFailure[CollectionRuntimeAction.RefreshPlanner].LastProgressAtUtc);
            Assert.IsNotNull(beforeFailure[CollectionRuntimeAction.DiscoveryPlanner].LastProgressAtUtc);

            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
                await db.Database.ExecuteSqlRawAsync("DROP TABLE collection_platform_controls");

            await Assert.ThrowsAsync<SqliteException>(() => coordinator.RunCycleAsync(CancellationToken.None));

            var afterFailure = runtime.GetSnapshot().Runtime.Actions
                .Where(x => x.Action is CollectionRuntimeAction.RefreshPlanner or CollectionRuntimeAction.DiscoveryPlanner)
                .ToDictionary(x => x.Action);
            foreach (var action in new[] { CollectionRuntimeAction.RefreshPlanner, CollectionRuntimeAction.DiscoveryPlanner })
            {
                Assert.AreEqual(CollectionRuntimeState.Error, afterFailure[action].State);
                Assert.AreEqual(CollectionRuntimeReason.Error, afterFailure[action].Reason);
                Assert.AreEqual(1, afterFailure[action].ConsecutiveErrors);
                Assert.AreEqual(beforeFailure[action].LastSuccessfulCycleAtUtc, afterFailure[action].LastSuccessfulCycleAtUtc);
                Assert.AreEqual(beforeFailure[action].LastProgressAtUtc, afterFailure[action].LastProgressAtUtc);
                Assert.AreEqual(0, afterFailure[action].InspectedCount);
                Assert.AreEqual(0, afterFailure[action].CreatedCount);
            }

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => coordinator.RunCycleAsync(cancelled.Token));

            var afterCancellation = runtime.GetSnapshot().Runtime.Actions
                .Where(x => x.Action is CollectionRuntimeAction.RefreshPlanner or CollectionRuntimeAction.DiscoveryPlanner);
            Assert.IsTrue(afterCancellation.All(x => x.ConsecutiveErrors == 1),
                "A canceled preflight read is not a second business failure.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task HostedPreflightFailureWaitsForNormalCadenceBeforeRetrying()
    {
        var (directory, store, now) = await CreateStoreWithDueResourcesAsync(1);
        try
        {
            var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False").Options;
            await using (var db = new CollectionPlatformDbContext(dbOptions))
                await db.Database.ExecuteSqlRawAsync("DROP TABLE collection_platform_controls");

            var clock = new ManualTimeProvider(now.ToUniversalTime());
            var runtime = new CollectionRuntimeStatusRecorder(Enum.GetValues<CollectionRuntimeAction>().Select(action =>
                new CollectionRuntimeActionConfiguration(action, true,
                    action == CollectionRuntimeAction.MetricDelivery ? null : TimeSpan.FromMinutes(1))));
            var coordinator = new CollectionMaintenanceCoordinator(store,
                new CollectionScheduleService(store, [new AlwaysDuePolicy()],
                    NullLogger<CollectionScheduleService>.Instance, runtime),
                new CollectionPlanningScheduler(store, runtime), clock,
                NullLogger<CollectionMaintenanceCoordinator>.Instance, runtime);

            await coordinator.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await clock.WaitForTimerCountAsync(1);
                Assert.AreEqual(CollectionBackgroundRuntimeOptions.ProducerCadence, clock.LastScheduledDelay);
                AssertPlannerErrors(runtime, 1);

                clock.Advance(CollectionBackgroundRuntimeOptions.ProducerCadence - TimeSpan.FromSeconds(1));
                Assert.AreEqual(1, clock.TimerCount,
                    "The failed preflight must not schedule a one-second hot retry before normal cadence.");
                AssertPlannerErrors(runtime, 1);

                clock.Advance(TimeSpan.FromSeconds(1));
                await clock.WaitForTimerCountAsync(2);
                AssertPlannerErrors(runtime, 2);
            }
            finally
            {
                await coordinator.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertPlannerErrors(CollectionRuntimeStatusRecorder runtime, int expectedErrors)
    {
        var planners = runtime.GetSnapshot().Runtime.Actions
            .Where(x => x.Action is CollectionRuntimeAction.RefreshPlanner or CollectionRuntimeAction.DiscoveryPlanner)
            .ToArray();
        Assert.HasCount(2, planners);
        Assert.IsTrue(planners.All(x => x.State == CollectionRuntimeState.Error &&
            x.ConsecutiveErrors == expectedErrors));
    }

    [TestMethod]
    public async Task BudgetStopPreservesCursorAndClockJumpResetsItInTheCoordinator()
    {
        var (directory, store, now) = await CreateStoreWithDueResourcesAsync(3);
        try
        {
            var clock = new ManualTimeProvider(now.ToUniversalTime());
            var policy = new AdvancingRecordingPolicy(clock, TimeSpan.FromSeconds(31));
            var runtime = new CollectionRuntimeStatusRecorder(Enum.GetValues<CollectionRuntimeAction>().Select(action =>
                new CollectionRuntimeActionConfiguration(action, true,
                    action == CollectionRuntimeAction.MetricDelivery ? null : TimeSpan.FromMinutes(1))));
            var coordinator = new CollectionMaintenanceCoordinator(store,
                new CollectionScheduleService(store, [policy], NullLogger<CollectionScheduleService>.Instance, runtime),
                new CollectionPlanningScheduler(store, runtime), clock,
                NullLogger<CollectionMaintenanceCoordinator>.Instance, runtime);

            await coordinator.RunCycleAsync(CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "coordinator-000" }, policy.EvaluatedResourceIds);
            var firstRefresh = runtime.GetSnapshot().Runtime.Actions.Single(x =>
                x.Action == CollectionRuntimeAction.RefreshPlanner);
            Assert.AreEqual(CollectionRuntimeReason.BudgetExhausted, firstRefresh.Reason);
            Assert.AreEqual(0, firstRefresh.CreatedCount,
                "The policy skipped this inspected candidate, so scheduling intent is not progress.");
            Assert.IsNull(firstRefresh.LastProgressAtUtc);

            clock.Advance(TimeSpan.FromMinutes(1));
            await coordinator.RunCycleAsync(CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "coordinator-000", "coordinator-001", "coordinator-002" },
                policy.EvaluatedResourceIds);

            clock.JumpWall(TimeSpan.FromHours(1));
            await coordinator.RunCycleAsync(CancellationToken.None);

            CollectionAssert.AreEqual(new[]
            {
                "coordinator-000", "coordinator-001", "coordinator-002",
                "coordinator-000", "coordinator-001", "coordinator-002",
            }, policy.EvaluatedResourceIds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DelayedCycleDoesNotReplayEveryElapsedMinute()
    {
        var (directory, store, now) = await CreateStoreWithDueResourcesAsync(1);
        try
        {
            var clock = new ManualTimeProvider(now.ToUniversalTime());
            var policy = new RecordingPolicy();
            var coordinator = CreateCoordinator(store, policy, clock);
            await coordinator.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await clock.WaitForTimerCountAsync(1);
                clock.Advance(TimeSpan.FromMinutes(10));
                await clock.WaitForTimerCountAsync(2);

                Assert.AreEqual(2, policy.EvaluatedResourceIds.Count,
                    "An elapsed ten-minute interval is one due cycle, not ten catch-up cycles.");
            }
            finally
            {
                await coordinator.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CancellationBetweenCandidatesStopsTheRefreshSweep()
    {
        var (directory, store, now) = await CreateStoreWithDueResourcesAsync(2);
        try
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var policy = new BlockingFirstPolicy(entered, release);
            var coordinator = CreateCoordinator(store, policy, new ManualTimeProvider(now.ToUniversalTime()));
            using var cancellation = new CancellationTokenSource();
            var cycle = Task.Run(() => coordinator.RunCycleAsync(cancellation.Token));

            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancellation.Cancel();
                release.TrySetResult();

                await Assert.ThrowsAsync<OperationCanceledException>(() => cycle.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.AreEqual(1, policy.EvaluatedResourceIds.Count);
            }
            finally
            {
                release.TrySetResult();
                if (!cycle.IsCompleted)
                    await cycle.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task HostedCoordinatorDoesNotOverlapAProducerThatHasNotCompleted()
    {
        var (directory, store, now) = await CreateStoreWithDueResourcesAsync(1);
        try
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var policy = new BlockingFirstPolicy(entered, release);
            var clock = new ManualTimeProvider(now.ToUniversalTime());
            var coordinator = CreateCoordinator(store, policy, clock);
            var start = Task.Run(() => coordinator.StartAsync(CancellationToken.None));

            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                clock.Advance(TimeSpan.FromMinutes(5));
                Assert.AreEqual(1, policy.EvaluatedResourceIds.Count,
                    "The coordinator awaits its finite action before starting another cycle.");
            }
            finally
            {
                release.TrySetResult();
                await start.WaitAsync(TimeSpan.FromSeconds(5));
                await coordinator.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.AreEqual(1, policy.EvaluatedResourceIds.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class NeverDuePolicy : ICollectionSchedulePolicy
    {
        public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now) =>
            new(false, null, CollectionPriority.Normal, CollectionLane.Normal, "not due");
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly object _sync = new();
        private DateTimeOffset _utcNow = utcNow;
        private long _timestamp;
        private int _timerCount;
        private TimeSpan _lastScheduledDelay;
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

        internal int TimerCount
        {
            get { lock (_sync) return _timerCount; }
        }

        internal TimeSpan LastScheduledDelay
        {
            get { lock (_sync) return _lastScheduledDelay; }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            TaskCompletionSource changed;
            lock (_sync)
            {
                _timerCount++;
                _lastScheduledDelay = dueTime;
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

        internal void JumpWall(TimeSpan adjustment)
        {
            lock (_sync) _utcNow += adjustment;
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

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
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

    private static CollectionMaintenanceCoordinator CreateCoordinator(CollectionPlatformStore store,
        ICollectionSchedulePolicy policy, TimeProvider clock) => new(store,
        new CollectionScheduleService(store, [policy], NullLogger<CollectionScheduleService>.Instance),
        new CollectionPlanningScheduler(store), clock,
        NullLogger<CollectionMaintenanceCoordinator>.Instance);

    private static async Task<(string Directory, CollectionPlatformStore Store, DateTimeOffset Now)>
        CreateStoreWithDueResourcesAsync(int count)
    {
        var directory = Path.Combine(Path.GetTempPath(), "collection-maintenance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
        {
            StateDirectory = directory,
        }));
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", CollectionResourceType.Horse,
            1, "Initial", false);
        await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
            1, "Initial", false);
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(9));
        var resources = Enumerable.Range(0, count).Select(index => new CollectionResourceEntity
        {
            Type = CollectionResourceType.Horse,
            Provider = "JRA",
            ResourceId = $"coordinator-{index:D3}",
            AttributesJson = "{}",
            CreatedAt = now,
        }).ToArray();
        var dbOptions = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "collection-platform.db")};Pooling=False").Options;
        await using (var db = new CollectionPlatformDbContext(dbOptions))
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
        return (directory, store, now);
    }

    private sealed class RecordingPolicy : ICollectionSchedulePolicy
    {
        public List<string> EvaluatedResourceIds { get; } = [];

        public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now)
        {
            EvaluatedResourceIds.Add(resource.Id);
            return new(false, null, CollectionPriority.Normal, CollectionLane.Normal, "not due");
        }
    }

    private sealed class ThrowingPolicy : ICollectionSchedulePolicy
    {
        public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now) =>
            throw new InvalidOperationException("synthetic refresh failure");
    }

    private sealed class AlwaysDuePolicy : ICollectionSchedulePolicy
    {
        public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now) =>
            new(true, state.NextCollectionAt, CollectionPriority.Normal, CollectionLane.Normal, "due");
    }

    private sealed class AdvancingRecordingPolicy(ManualTimeProvider clock, TimeSpan firstAdvance)
        : ICollectionSchedulePolicy
    {
        public List<string> EvaluatedResourceIds { get; } = [];

        public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now)
        {
            EvaluatedResourceIds.Add(resource.Id);
            if (EvaluatedResourceIds.Count == 1) clock.Advance(firstAdvance);
            return new(false, null, CollectionPriority.Normal, CollectionLane.Normal, "not due");
        }
    }

    private sealed class BlockingFirstPolicy(TaskCompletionSource entered, TaskCompletionSource release)
        : ICollectionSchedulePolicy
    {
        public List<string> EvaluatedResourceIds { get; } = [];

        public CollectionSchedule Evaluate(ResourceKey resource, CollectionStateSnapshot state, DateTimeOffset now)
        {
            EvaluatedResourceIds.Add(resource.Id);
            if (EvaluatedResourceIds.Count == 1)
            {
                entered.TrySetResult();
                release.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            return new(false, null, CollectionPriority.Normal, CollectionLane.Normal, "not due");
        }
    }
}
