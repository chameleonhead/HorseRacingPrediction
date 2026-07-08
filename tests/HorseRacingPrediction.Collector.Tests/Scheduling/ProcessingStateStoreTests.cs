using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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