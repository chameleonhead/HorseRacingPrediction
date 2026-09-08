using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class CollectionExecutionControlTests
{
    private string directory = null!;
    private ProcessingStateStore store = null!;
    [TestInitialize] public void Setup()
    {
        directory = Path.Combine(Path.GetTempPath(), "collection-control-tests", Guid.NewGuid().ToString("N"));
        store = CreateStore();
    }
    private ProcessingStateStore CreateStore() => new(Options.Create(new AgentProcessingOptions { StateDirectory = directory, MaxConcurrentJobs = 3 }), NullLogger<ProcessingStateStore>.Instance);
    [TestCleanup] public void Cleanup() => Directory.Delete(directory, true);
    private async Task<LeasedCollectionTask> LeaseAsync(string key)
    {
        await store.ScheduleJobAsync(AgentJobType.RaceCardCollection, key, "{}", DateTimeOffset.UtcNow.AddSeconds(-1));
        return (await store.AcquireCollectionTaskAsync(AgentJobType.RaceCardCollection, key, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30)))!;
    }
    private async Task HoldAsync(string id, bool held = true)
    {
        var job = (await store.GetJobDetailAsync(id))!;
        Assert.AreEqual(ForceRequeueJobResult.Requeued, await store.SetJobHoldAsync(id, held, job.UpdatedAt, "test"));
    }

    [TestMethod] public async Task ReadyHold_PersistsAcrossRestartAndRejectsOldNotifications()
    {
        await store.ScheduleJobAsync(AgentJobType.RaceCardCollection, "ready", "{}", DateTimeOffset.UtcNow.AddSeconds(-1));
        var old = (await store.GetPendingCollectionTaskDispatchesAsync(DateTimeOffset.UtcNow, 10)).Single();
        await HoldAsync(old.Notification.TaskId);
        store = CreateStore();
        Assert.IsTrue((await store.GetJobDetailAsync(old.Notification.TaskId))!.IsHeld);
        Assert.IsEmpty(await store.GetPendingCollectionTaskDispatchesAsync(DateTimeOffset.UtcNow, 10));
        Assert.IsEmpty(await store.AcquireReadyJobsAsync(AgentJobType.RaceCardCollection, DateTimeOffset.UtcNow, TimeSpan.Zero, 1, TimeSpan.FromMinutes(30)));
        await store.ScheduleJobAsync(AgentJobType.RaceCardCollection, "ready", "{}", DateTimeOffset.UtcNow);
        await HoldAsync(old.Notification.TaskId, false);
        Assert.IsNull(await store.AcquireCollectionTaskAsync(AgentJobType.RaceCardCollection, "ready", old.Notification.DispatchGeneration, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30)));
        Assert.IsNotNull(await store.AcquireCollectionTaskAsync(AgentJobType.RaceCardCollection, "ready", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30)));
    }

    [TestMethod] public async Task RunningHold_CancelsThenAcknowledgesWithoutFailingOrPausingOtherJobs()
    {
        var task = await LeaseAsync("running");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = false;
        var run = CollectionTaskRunner.RunAsync(store, task, async token =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cleaned = true; }
        }, TimeSpan.FromMinutes(1), true, "hold-request", CancellationToken.None);
        await started.Task;
        await HoldAsync(task.TaskId);
        var pending = (await store.GetJobDetailAsync(task.TaskId))!;
        Assert.AreEqual(ForceRequeueJobResult.Conflict, await store.SetJobHoldAsync(task.TaskId, false, pending.UpdatedAt, "test"));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(cleaned);
        var held = (await store.GetJobDetailAsync(task.TaskId))!;
        Assert.IsTrue(held.IsHeld);
        Assert.AreEqual(AgentJobStatus.Ready, held.Status);
        Assert.AreEqual(AgentJobStatus.Cancelled, held.Attempts.Single().Status);
        Assert.IsFalse((await store.GetCollectionPipelineStateAsync()).IsPaused);
        Assert.IsNotNull(await LeaseAsync("other"));
        await HoldAsync(task.TaskId, false);
        Assert.IsFalse(await store.CompleteCollectionTaskAsync(task.JobType, task.DeduplicationKey, task.LeaseToken));
    }

    [TestMethod] public async Task Timeout_AtomicallyFailsAndStopsNewLeasesButAcceptsExistingResults()
    {
        var timedOut = await LeaseAsync("timeout");
        var other = await LeaseAsync("already-running");
        await store.ScheduleJobAsync(AgentJobType.RaceCardCollection, "next", "{}", DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<TimeoutException>(() => CollectionTaskRunner.RunAsync(store, timedOut,
            token => Task.Delay(Timeout.Infinite, token), TimeSpan.FromMilliseconds(50), false, "request-123", CancellationToken.None));
        var failed = (await store.GetJobDetailAsync(timedOut.TaskId))!;
        Assert.AreEqual(AgentJobStatus.Failed, failed.Status);
        StringAssert.Contains(failed.LastError!, "request-123");
        Assert.HasCount(1, failed.Attempts);
        store = CreateStore();
        Assert.IsTrue((await store.GetCollectionPipelineStateAsync()).IsPaused);
        Assert.IsNull(await store.AcquireCollectionTaskAsync(AgentJobType.RaceCardCollection, "next", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30)));
        Assert.IsEmpty(await store.GetPendingCollectionTaskDispatchesAsync(DateTimeOffset.UtcNow, 10));
        Assert.IsTrue(await store.CompleteCollectionTaskAsync(other.JobType, other.DeduplicationKey, other.LeaseToken));
        await store.ResumeCollectionAsync();
        Assert.AreEqual(AgentJobStatus.Failed, (await store.GetJobDetailAsync(timedOut.TaskId))!.Status);
        Assert.IsNotNull(await store.AcquireCollectionTaskAsync(AgentJobType.RaceCardCollection, "next", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30)));
    }

    [TestMethod] public async Task HoldWinsRaceWithCompletionAndTimeout()
    {
        var task = await LeaseAsync("race");
        await HoldAsync(task.TaskId);
        Assert.IsFalse(await store.CompleteCollectionTaskAsync(task.JobType, task.DeduplicationKey, task.LeaseToken));
        Assert.IsFalse(await store.FailAndPauseCollectionTaskAsync(task.JobType, task.DeduplicationKey, task.LeaseToken, "timeout"));
        Assert.IsFalse((await store.GetCollectionPipelineStateAsync()).IsPaused);
        Assert.AreEqual(AgentJobStatus.Running, (await store.GetJobDetailAsync(task.TaskId))!.Status);
        Assert.IsTrue(await store.AcknowledgeCollectionHoldAsync(task.TaskId, task.LeaseToken));
        Assert.IsFalse(await store.AcknowledgeCollectionHoldAsync(task.TaskId, task.LeaseToken));
    }

    [TestMethod] public async Task ParentHold_DoesNotHoldChildAndReconcilesOnlyAfterRelease()
    {
        await store.ScheduleJobAsync("Parent", "parent", "{}", DateTimeOffset.UtcNow);
        await store.ScheduleJobAsync("Child", "child", "{}", DateTimeOffset.UtcNow, parentJobId: "Parent:parent");
        await store.WaitForDependenciesAsync("Parent", "parent");
        await HoldAsync("Parent:parent");
        await store.CompleteJobAsync("Child", "child");
        Assert.AreEqual(AgentJobStatus.WaitingDependency, (await store.GetJobDetailAsync("Parent:parent"))!.Status);
        Assert.IsFalse((await store.GetJobDetailAsync("Child:child"))!.IsHeld);
        await store.PauseCollectionAsync("test", null);
        await store.ResumeCollectionAsync();
        Assert.IsTrue((await store.GetJobDetailAsync("Parent:parent"))!.IsHeld);
        await HoldAsync("Parent:parent", false);
        Assert.AreEqual(AgentJobStatus.Succeeded, (await store.GetJobDetailAsync("Parent:parent"))!.Status);
    }

    [TestMethod] public async Task HostShutdown_RequeuesWithoutStoppingPipeline()
    {
        var task = await LeaseAsync("shutdown");
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => CollectionTaskRunner.RunAsync(store, task,
            token => { stop.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            TimeSpan.FromMinutes(1), false, null, stop.Token));
        Assert.IsFalse((await store.GetCollectionPipelineStateAsync()).IsPaused);
        Assert.AreEqual(AgentJobStatus.Ready, (await store.GetJobDetailAsync(task.TaskId))!.Status);
    }

    [TestMethod] public async Task ExistingDatabaseMigration_PreservesJobsAndDefaultsToNotHeld()
    {
        await store.ScheduleJobAsync("Collection", "existing", "payload", DateTimeOffset.UtcNow);
        using (var db = new SqliteConnection($"Data Source={Path.Combine(directory, "processing-jobs.db")};Pooling=False"))
        {
            db.Open();
            using var command = db.CreateCommand(); command.CommandText = "ALTER TABLE jobs DROP COLUMN is_held"; command.ExecuteNonQuery();
        }
        store = CreateStore();
        var job = (await store.GetJobDetailAsync("Collection:existing"))!;
        Assert.IsFalse(job.IsHeld); Assert.AreEqual("payload", job.Payload);
        await HoldAsync(job.JobId);
    }

    [TestMethod] public async Task ExpiredHeldLease_IsAcknowledgedWithoutAutomaticExecution()
    {
        var task = await LeaseAsync("expired");
        await HoldAsync(task.TaskId);
        await store.RequeueRunningJobsAsync([task.JobType], DateTimeOffset.UtcNow.AddHours(1));
        var held = (await store.GetJobDetailAsync(task.TaskId))!;
        Assert.IsTrue(held.IsHeld);
        Assert.AreEqual(AgentJobStatus.Ready, held.Status);
        Assert.AreEqual(AgentJobStatus.Cancelled, held.Attempts.Single().Status);
        Assert.IsEmpty(await store.GetPendingCollectionTaskDispatchesAsync(DateTimeOffset.UtcNow.AddHours(2), 10));
        Assert.IsFalse(await store.CompleteCollectionTaskAsync(task.JobType, task.DeduplicationKey, task.LeaseToken));
        await HoldAsync(task.TaskId, false);
        Assert.IsNotNull(await store.AcquireCollectionTaskAsync(task.JobType, task.DeduplicationKey, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30)));
    }

    [TestMethod] public async Task HoldBeforeRunnerBegins_DoesNotExecuteWork()
    {
        var task = await LeaseAsync("before-start");
        await HoldAsync(task.TaskId);
        var executed = false;
        await CollectionTaskRunner.RunAsync(store, task, _ => { executed = true; return Task.CompletedTask; },
            TimeSpan.FromMinutes(1), true, null, CancellationToken.None);
        Assert.IsFalse(executed);
        Assert.AreEqual(AgentJobStatus.Ready, (await store.GetJobDetailAsync(task.TaskId))!.Status);
    }

    [TestMethod] public async Task CompletedJob_RejectsLateHold()
    {
        var task = await LeaseAsync("completed");
        var running = (await store.GetJobDetailAsync(task.TaskId))!;
        await store.CompleteCollectionTaskAsync(task.JobType, task.DeduplicationKey, task.LeaseToken);
        Assert.AreEqual(ForceRequeueJobResult.Conflict, await store.SetJobHoldAsync(task.TaskId, true, running.UpdatedAt, "test"));
        Assert.IsFalse((await store.GetJobDetailAsync(task.TaskId))!.IsHeld);
    }
}
