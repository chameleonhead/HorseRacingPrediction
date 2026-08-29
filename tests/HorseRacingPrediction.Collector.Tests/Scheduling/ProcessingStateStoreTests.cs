using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class ProcessingStateStoreTests
{
    private string _stateDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _stateDirectory = Path.Combine(Path.GetTempPath(), "agent-client-state-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stateDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task TakeReadyPredictionCandidatesAsync_DoesNotLoseCandidateAcrossRestart_WhenLeaseExpires()
    {
        var now = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);

        await sut.EnqueuePredictionCandidatesAsync(["race-1"], now);

        var firstTake = await sut.TakeReadyPredictionCandidatesAsync(now.AddMinutes(11), TimeSpan.FromMinutes(10), 10);

        CollectionAssert.AreEqual(new[] { "race-1" }, firstTake.ToArray());

        var restarted = CreateStore(predictionLeaseMinutes: 5);
        var beforeLeaseExpiry = await restarted.TakeReadyPredictionCandidatesAsync(now.AddMinutes(15), TimeSpan.FromMinutes(10), 10);
        Assert.IsEmpty(beforeLeaseExpiry, "lease 中は再取得されないこと");

        var afterLeaseExpiry = await restarted.TakeReadyPredictionCandidatesAsync(now.AddMinutes(17), TimeSpan.FromMinutes(10), 10);
        CollectionAssert.AreEqual(new[] { "race-1" }, afterLeaseExpiry.ToArray());
    }

    [TestMethod]
    public async Task MarkPredictionCompletedAsync_RemovesInProgressEntry()
    {
        var now = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);

        await sut.EnqueuePredictionCandidatesAsync(["race-1"], now);
        await sut.TakeReadyPredictionCandidatesAsync(now.AddMinutes(11), TimeSpan.FromMinutes(10), 10);

        await sut.MarkPredictionCompletedAsync("race-1");

        var restarted = CreateStore(predictionLeaseMinutes: 5);
        var afterCompletion = await restarted.TakeReadyPredictionCandidatesAsync(now.AddMinutes(30), TimeSpan.FromMinutes(10), 10);

        Assert.IsEmpty(afterCompletion, "完了済みは再取得されないこと");
    }

    [TestMethod]
    public async Task ScheduleJobAsync_RequeuesSucceededRecurringJob()
    {
        var now = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);

        await sut.ScheduleJobAsync("RecurringCollection", "2026-05-12", "payload-1", now, priority: 10);
        var firstTake = await sut.AcquireReadyJobsAsync(
            "RecurringCollection",
            now,
            TimeSpan.Zero,
            10,
            TimeSpan.FromMinutes(5));
        CollectionAssert.AreEqual(new[] { "payload-1" }, firstTake.Select(x => x.Payload).ToArray());

        await sut.CompleteJobAsync("RecurringCollection", "2026-05-12");
        await sut.ScheduleJobAsync("RecurringCollection", "2026-05-12", "payload-2", now.AddMinutes(30), priority: 10);

        var secondTake = await sut.AcquireReadyJobsAsync(
            "RecurringCollection",
            now.AddMinutes(30),
            TimeSpan.Zero,
            10,
            TimeSpan.FromMinutes(5));

        CollectionAssert.AreEqual(new[] { "payload-2" }, secondTake.Select(x => x.Payload).ToArray());
    }

    [TestMethod]
    public async Task MarkJobAsDeadLetterAsync_ExcludesJobFromActivePayloads()
    {
        var now = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);

        await sut.ScheduleJobAsync("HistoricalRequest", "job-1", "payload-1", now, priority: 10);
        await sut.MarkJobAsDeadLetterAsync("HistoricalRequest", "job-1", "not implemented");

        var activePayloads = await sut.GetActiveJobPayloadsAsync("HistoricalRequest");

        Assert.IsEmpty(activePayloads);
    }

    [TestMethod]
    public async Task AcquireReadyJobsAsync_RespectsGlobalMaxConcurrentJobs()
    {
        var now = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5, maxConcurrentJobs: 1);

        await sut.ScheduleJobAsync("JobType-A", "a-1", "payload-a", now, priority: 100);
        await sut.ScheduleJobAsync("JobType-B", "b-1", "payload-b", now, priority: 90);

        var firstTake = await sut.AcquireReadyJobsAsync(
            "JobType-A",
            now,
            TimeSpan.Zero,
            10,
            TimeSpan.FromMinutes(5));
        CollectionAssert.AreEqual(new[] { "payload-a" }, firstTake.Select(x => x.Payload).ToArray());

        var blockedTake = await sut.AcquireReadyJobsAsync(
            "JobType-B",
            now,
            TimeSpan.Zero,
            10,
            TimeSpan.FromMinutes(5));
        Assert.IsEmpty(blockedTake);

        await sut.CompleteJobAsync("JobType-A", "a-1");

        var secondTake = await sut.AcquireReadyJobsAsync(
            "JobType-B",
            now.AddMinutes(1),
            TimeSpan.Zero,
            10,
            TimeSpan.FromMinutes(5));
        CollectionAssert.AreEqual(new[] { "payload-b" }, secondTake.Select(x => x.Payload).ToArray());
    }

    [TestMethod]
    public async Task ForceRequeueJobAsync_ReactivatesDeadLetterJob()
    {
        var now = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5, maxConcurrentJobs: 1);

        await sut.ScheduleJobAsync("JobType-C", "c-1", "payload-c", now, priority: 80);
        await sut.MarkJobAsDeadLetterAsync("JobType-C", "c-1", "failed permanently");

        var requeued = await sut.ForceRequeueJobAsync("JobType-C", "c-1", now.AddMinutes(2));

        Assert.IsTrue(requeued);

        var taken = await sut.AcquireReadyJobsAsync(
            "JobType-C",
            now.AddMinutes(2),
            TimeSpan.Zero,
            10,
            TimeSpan.FromMinutes(5));
        CollectionAssert.AreEqual(new[] { "payload-c" }, taken.Select(x => x.Payload).ToArray());
    }

    [TestMethod]
    public async Task ScheduleJobAsync_ReactivatesDeadLetterJobAndReplacesPayload()
    {
        var now = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5, maxConcurrentJobs: 1);

        await sut.ScheduleJobAsync("JobType-D", "d-1", "payload-old", now, priority: 70);
        await sut.MarkJobAsDeadLetterAsync("JobType-D", "d-1", "failed permanently");

        await sut.ScheduleJobAsync("JobType-D", "d-1", "payload-new", now.AddMinutes(3), priority: 95);

        var taken = await sut.AcquireReadyJobsAsync(
            "JobType-D",
            now.AddMinutes(3),
            TimeSpan.Zero,
            10,
            TimeSpan.FromMinutes(5));
        CollectionAssert.AreEqual(new[] { "payload-new" }, taken.Select(x => x.Payload).ToArray());
    }

    [TestMethod]
    public async Task ScheduleJobAsync_CreatesDispatchOutboxWithTask()
    {
        var now = new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);

        await sut.ScheduleJobAsync("RaceCardCollection", "JRA:race-card:2026-08-30", "{}", now, priority: 100);

        var dispatches = await sut.GetPendingCollectionTaskDispatchesAsync(now, 10);
        CollectionAssert.AreEqual(
            new[] { "RaceCardCollection" },
            dispatches.Select(x => x.Notification.JobType).ToArray());
        Assert.AreEqual("RaceCardCollection", dispatches[0].Notification.JobType);
        Assert.AreEqual("JRA:race-card:2026-08-30", dispatches[0].Notification.DeduplicationKey);
    }

    [TestMethod]
    public async Task CompleteCollectionTaskAsync_RejectsStaleLeaseToken()
    {
        var now = new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);
        await sut.ScheduleJobAsync("RaceCardCollection", "job-lease", "{}", now);
        var task = await sut.AcquireCollectionTaskAsync("RaceCardCollection", "job-lease", now, TimeSpan.FromMinutes(10));

        Assert.IsNotNull(task);
        Assert.IsFalse(await sut.CompleteCollectionTaskAsync("RaceCardCollection", "job-lease", "stale-token"));
        Assert.IsTrue(await sut.CompleteCollectionTaskAsync("RaceCardCollection", "job-lease", task.LeaseToken));
    }

    [TestMethod]
    public async Task FailCollectionTaskAsync_MarksTaskFailedWithoutRedispatch()
    {
        var now = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);
        await sut.ScheduleJobAsync("RaceCardCollection", "job-fast-fail", "{}", now);
        var dispatches = await sut.GetPendingCollectionTaskDispatchesAsync(now, 10);
        Assert.HasCount(1, dispatches);
        var dispatch = dispatches[0];
        await sut.MarkCollectionTaskDispatchedAsync(dispatch.OutboxId, now);
        var task = await sut.AcquireCollectionTaskAsync("RaceCardCollection", "job-fast-fail", now, TimeSpan.FromMinutes(10));

        Assert.IsNotNull(task);
        Assert.IsTrue(await sut.FailCollectionTaskAsync(
            task.JobType,
            task.DeduplicationKey,
            task.LeaseToken,
            "fatal error"));

        var statuses = await sut.GetJobStatusesAsync("RaceCardCollection", AgentJobStatus.Failed, 10);
        Assert.HasCount(1, statuses);
        var status = statuses[0];
        Assert.AreEqual("fatal error", status.LastError);
        Assert.AreEqual(0, status.AttemptCount);
        Assert.IsEmpty(await sut.GetPendingCollectionTaskDispatchesAsync(now.AddHours(1), 10));
    }

    [TestMethod]
    public async Task ScheduleJobAsync_DoesNotAutomaticallyReactivateFailedJob()
    {
        var now = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);
        await sut.ScheduleJobAsync("ResultMonthDiscoveryRequest", "JRA:result-month:2026-08", "{}", now);
        await sut.FailJobAsync("ResultMonthDiscoveryRequest", "JRA:result-month:2026-08", "fatal error");

        await sut.ScheduleJobAsync(
            "ResultMonthDiscoveryRequest",
            "JRA:result-month:2026-08",
            "{\"updated\":true}",
            now.AddMinutes(1));

        var failed = await sut.GetJobStatusesAsync("ResultMonthDiscoveryRequest", AgentJobStatus.Failed, 10);
        Assert.HasCount(1, failed);
        Assert.AreEqual("fatal error", failed[0].LastError);
    }

    [TestMethod]
    public async Task ForceRequeueJobAsync_ByJobIdRejectsStaleUpdate()
    {
        var now = new DateTimeOffset(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);
        await sut.ScheduleJobAsync("JobType-Concurrency", "item-1", "{}", now, priority: 10);
        var job = (await sut.GetJobStatusesAsync("JobType-Concurrency", null, 10)).Single();

        var requeued = await sut.ForceRequeueJobAsync(job.JobId, job.UpdatedAt, now.AddMinutes(1));
        var stale = await sut.ForceRequeueJobAsync(job.JobId, job.UpdatedAt, now.AddMinutes(2));
        var detail = await sut.GetJobDetailAsync(job.JobId);

        Assert.AreEqual(ForceRequeueJobResult.Requeued, requeued);
        Assert.AreEqual(ForceRequeueJobResult.Conflict, stale);
        Assert.IsNotNull(detail);
        Assert.HasCount(1, detail.AuditHistory);
        Assert.AreEqual("ManualRequeue", detail.AuditHistory[0].Operation);
        Assert.AreEqual("admin-ui", detail.AuditHistory[0].ActorId);
    }

    [TestMethod]
    public async Task ScheduleJobAsync_ExposesParentAndChildJobsInDetail()
    {
        var now = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);
        await sut.ScheduleJobAsync("Parent", "parent-1", "{}", now);
        var parentId = ProcessingStateStore.ComposeJobId("Parent", "parent-1");
        await sut.ScheduleJobAsync("Child", "child-1", "{}", now.AddMinutes(1), parentJobId: parentId);

        var parent = await sut.GetJobDetailAsync(parentId);
        var child = await sut.GetJobDetailAsync(ProcessingStateStore.ComposeJobId("Child", "child-1"));

        Assert.IsNotNull(parent);
        Assert.HasCount(1, parent.ChildJobs);
        Assert.AreEqual("Child", parent.ChildJobs[0].JobType);
        Assert.IsNotNull(child);
        Assert.AreEqual(parentId, child.ParentJob?.JobId);
    }

    [TestMethod]
    public async Task FailJobAsync_CreatesSingleFailureNotificationForStateTransition()
    {
        var now = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);
        await sut.ScheduleJobAsync("RaceCardCollection", "failed-job", "{}", now);

        await sut.FailJobAsync("RaceCardCollection", "failed-job", "browser failed");
        await sut.FailJobAsync("RaceCardCollection", "failed-job", "browser failed again");

        var notifications = await sut.GetPendingJobFailureNotificationsAsync(DateTimeOffset.UtcNow.AddMinutes(1), 10);
        Assert.HasCount(1, notifications);
        Assert.AreEqual("RaceCardCollection", notifications[0].JobType);
        Assert.AreEqual("failed-job", notifications[0].DeduplicationKey);
        Assert.AreEqual("browser failed", notifications[0].Error);
        Assert.AreEqual(nameof(AgentJobStatus.Failed), notifications[0].Status);
    }

    [TestMethod]
    public async Task FailedJob_NotifiesAgainAfterManualRequeueAndSecondFailure()
    {
        var now = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);
        await sut.ScheduleJobAsync("RaceCardCollection", "retry-job", "{}", now);
        await sut.FailJobAsync("RaceCardCollection", "retry-job", "first failure");
        var first = await sut.GetPendingJobFailureNotificationsAsync(DateTimeOffset.UtcNow.AddMinutes(1), 10);
        await sut.MarkJobFailureNotificationPublishedAsync(first[0].NotificationId, DateTimeOffset.UtcNow);

        Assert.IsTrue(await sut.ForceRequeueJobAsync("RaceCardCollection", "retry-job", now.AddMinutes(1)));
        await sut.FailJobAsync("RaceCardCollection", "retry-job", "second failure");

        var second = await sut.GetPendingJobFailureNotificationsAsync(DateTimeOffset.UtcNow.AddMinutes(1), 10);
        Assert.HasCount(1, second);
        Assert.AreEqual("second failure", second[0].Error);
        Assert.AreNotEqual(first[0].NotificationId, second[0].NotificationId);
    }

    [TestMethod]
    public async Task FailCollectionTaskAsync_CreatesFailureNotificationWithLeaseProtectedUpdate()
    {
        var now = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);
        await sut.ScheduleJobAsync("RaceCardCollection", "leased-failure", "{}", now);
        var task = await sut.AcquireCollectionTaskAsync("RaceCardCollection", "leased-failure", now, TimeSpan.FromMinutes(10));
        Assert.IsNotNull(task);

        Assert.IsTrue(await sut.FailCollectionTaskAsync(task.JobType, task.DeduplicationKey, task.LeaseToken, "leased error"));

        var notifications = await sut.GetPendingJobFailureNotificationsAsync(DateTimeOffset.UtcNow.AddMinutes(1), 10);
        Assert.HasCount(1, notifications);
        Assert.AreEqual("leased error", notifications[0].Error);
    }

    [TestMethod]
    public async Task ChildJobs_AllSucceeded_CompletesParentAndResultDay()
    {
        var now = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
        var date = new DateOnly(2026, 8, 15);
        var sut = CreateStore(predictionLeaseMinutes: 5);
        var parentKey = AgentJobKeyFactory.BuildResultDayCollectionRequestKey("JRA", date);
        await sut.ScheduleJobAsync(AgentJobType.ResultDayCollectionRequest, parentKey, "{}", now);
        var parentId = $"{AgentJobType.ResultDayCollectionRequest}:{parentKey}";
        await sut.UpsertResultDayCollectionStatusAsync("JRA", date, ResultDayCollectionState.Running, 2, 0, null, null, null, null, now);
        await sut.ScheduleJobAsync("Child", "race-1", "{}", now, parentJobId: parentId);
        await sut.ScheduleJobAsync("Child", "race-2", "{}", now, parentJobId: parentId);
        await sut.WaitForDependenciesAsync(AgentJobType.ResultDayCollectionRequest, parentKey);

        await sut.CompleteJobAsync("Child", "race-1");
        Assert.AreEqual(AgentJobStatus.WaitingDependency, (await sut.GetJobDetailAsync(parentId))!.Status);
        await sut.CompleteJobAsync("Child", "race-2");

        Assert.AreEqual(AgentJobStatus.Succeeded, (await sut.GetJobDetailAsync(parentId))!.Status);
        var day = await sut.GetResultDayCollectionStatusAsync("JRA", date);
        Assert.IsNotNull(day);
        Assert.AreEqual(ResultDayCollectionState.Complete, day.Status);
        Assert.AreEqual(2, day.CompletedRaceCount);
    }

    [TestMethod]
    public async Task ChildJobs_WithFailure_FailsParentAndRecordsProgress()
    {
        var now = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
        var date = new DateOnly(2026, 8, 15);
        var sut = CreateStore(predictionLeaseMinutes: 5);
        var parentKey = AgentJobKeyFactory.BuildResultDayCollectionRequestKey("JRA", date);
        await sut.ScheduleJobAsync(AgentJobType.ResultDayCollectionRequest, parentKey, "{}", now);
        var parentId = $"{AgentJobType.ResultDayCollectionRequest}:{parentKey}";
        await sut.UpsertResultDayCollectionStatusAsync("JRA", date, ResultDayCollectionState.Running, 2, 0, null, null, null, null, now);
        await sut.ScheduleJobAsync("Child", "race-1", "{}", now, parentJobId: parentId);
        await sut.ScheduleJobAsync("Child", "race-2", "{}", now, parentJobId: parentId);
        await sut.WaitForDependenciesAsync(AgentJobType.ResultDayCollectionRequest, parentKey);

        await sut.CompleteJobAsync("Child", "race-1");
        await sut.FailJobAsync("Child", "race-2", "scraping failed");

        var parent = await sut.GetJobDetailAsync(parentId);
        Assert.AreEqual(AgentJobStatus.Failed, parent!.Status);
        StringAssert.Contains(parent.LastError, "scraping failed");
        var day = await sut.GetResultDayCollectionStatusAsync("JRA", date);
        Assert.IsNotNull(day);
        Assert.AreEqual(ResultDayCollectionState.Incomplete, day.Status);
        Assert.AreEqual(1, day.CompletedRaceCount);
        StringAssert.Contains(day.LastError, "scraping failed");
    }

    [TestMethod]
    public async Task ScheduleJobAsync_DoesNotRequeueSucceededChildForSameParent()
    {
        var now = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
        var sut = CreateStore(predictionLeaseMinutes: 5);
        await sut.ScheduleJobAsync("Parent", "day", "{}", now);
        const string parentId = "Parent:day";
        await sut.ScheduleJobAsync("Child", "race-1", "old", now, parentJobId: parentId);
        await sut.CompleteJobAsync("Child", "race-1");

        await sut.ScheduleJobAsync("Child", "race-1", "new", now.AddMinutes(1), parentJobId: parentId);

        var child = await sut.GetJobDetailAsync("Child:race-1");
        Assert.IsNotNull(child);
        Assert.AreEqual(AgentJobStatus.Succeeded, child.Status);
        Assert.IsEmpty(await sut.AcquireReadyJobsAsync("Child", now.AddMinutes(2), TimeSpan.Zero, 1, TimeSpan.FromMinutes(5)));
    }

    [TestMethod]
    public async Task Constructor_AddsLeaseTokenColumnToExistingJobStore()
    {
        var databasePath = Path.Combine(_stateDirectory, "processing-jobs.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE jobs (
                    job_id TEXT NOT NULL PRIMARY KEY, job_type TEXT NOT NULL,
                    deduplication_key TEXT NOT NULL, payload TEXT NOT NULL, status TEXT NOT NULL,
                    priority INTEGER NOT NULL, first_queued_at TEXT NOT NULL, available_at TEXT NOT NULL,
                    started_at TEXT NULL, lease_expires_at TEXT NULL, attempt_count INTEGER NOT NULL,
                    last_error TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var sut = CreateStore(predictionLeaseMinutes: 5);
        var now = DateTimeOffset.UtcNow;
        await sut.ScheduleJobAsync("RaceCardCollection", "migration-task", "{}", now);

        var leased = await sut.AcquireCollectionTaskAsync(
            "RaceCardCollection", "migration-task", now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(leased);
        Assert.IsFalse(string.IsNullOrWhiteSpace(leased.LeaseToken));
    }

    [TestMethod]
    public async Task AcquireCollectionTaskAsync_RejectsNotificationFromPreviousGeneration()
    {
        var sut = CreateStore(5);
        var now = DateTimeOffset.UtcNow;
        await sut.ScheduleJobAsync("RaceCardCollection", "generation-task", "{}", now);
        var first = (await sut.GetPendingCollectionTaskDispatchesAsync(now.AddSeconds(1), 10)).Single();
        var detail = await sut.GetJobDetailAsync(first.Notification.TaskId);
        Assert.IsNotNull(detail);

        var result = await sut.ForceRequeueJobAsync(detail.JobId, detail.UpdatedAt, now.AddSeconds(2));
        Assert.AreEqual(ForceRequeueJobResult.Requeued, result);
        var dispatches = await sut.GetPendingCollectionTaskDispatchesAsync(now.AddSeconds(3), 10);
        var latest = dispatches.MaxBy(x => x.Notification.DispatchGeneration)!;

        var staleLease = await sut.AcquireCollectionTaskAsync(
            first.Notification.JobType, first.Notification.DeduplicationKey, first.Notification.DispatchGeneration,
            now.AddSeconds(4), TimeSpan.FromMinutes(5));
        Assert.IsNull(staleLease);

        var currentLease = await sut.AcquireCollectionTaskAsync(
            latest.Notification.JobType, latest.Notification.DeduplicationKey, latest.Notification.DispatchGeneration,
            now.AddSeconds(4), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(currentLease);
    }

    [TestMethod]
    public async Task CancelJobAsync_InvalidatesRunningLease()
    {
        var sut = CreateStore(5);
        var now = DateTimeOffset.UtcNow;
        await sut.ScheduleJobAsync("RaceCardCollection", "cancel-task", "{}", now);
        var dispatch = (await sut.GetPendingCollectionTaskDispatchesAsync(now.AddSeconds(1), 10)).Single();
        var lease = await sut.AcquireCollectionTaskAsync(
            dispatch.Notification.JobType, dispatch.Notification.DeduplicationKey, dispatch.Notification.DispatchGeneration,
            now.AddSeconds(2), TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        var detail = await sut.GetJobDetailAsync(dispatch.Notification.TaskId);
        Assert.IsNotNull(detail);

        var result = await sut.CancelJobAsync(detail.JobId, detail.UpdatedAt, "test", "stuck", now.AddSeconds(3));

        Assert.AreEqual(ForceRequeueJobResult.Requeued, result);
        Assert.IsFalse(await sut.CompleteCollectionTaskAsync(lease.JobType, lease.DeduplicationKey, lease.LeaseToken));
        Assert.AreEqual(AgentJobStatus.Cancelled, (await sut.GetJobDetailAsync(detail.JobId))!.Status);
    }

    [TestMethod]
    public async Task BackupAndResetAsync_CreatesVerifiedBackupAndEmptyCurrentStore()
    {
        var sut = CreateStore(5);
        var now = DateTimeOffset.UtcNow;
        await sut.ScheduleJobAsync("RaceCardCollection", "reset-task", "{}", now);
        var backupDirectory = Path.Combine(_stateDirectory, "backups", "full-reset-test");

        var backupPath = await sut.BackupAndResetAsync(backupDirectory);

        Assert.IsTrue(File.Exists(backupPath));
        Assert.IsEmpty(await sut.GetJobStatusesAsync(null, null, 100));
        await using var connection = new SqliteConnection($"Data Source={backupPath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM jobs;";
        Assert.AreEqual(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    private ProcessingStateStore CreateStore(int predictionLeaseMinutes, int maxConcurrentJobs = 1)
    {
        var options = Options.Create(new AgentProcessingOptions
        {
            StateDirectory = _stateDirectory,
            PredictionLeaseMinutes = predictionLeaseMinutes,
            MaxConcurrentJobs = maxConcurrentJobs,
        });

        return new ProcessingStateStore(options, NullLogger<ProcessingStateStore>.Instance);
    }
}
