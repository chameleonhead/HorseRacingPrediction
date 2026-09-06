using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

/// <summary>
/// <see cref="CollectionExecutionService"/> の統合テスト。実DB(SQLite)を使った
/// <see cref="ProcessingStateStore"/> と、新Jra層(<see cref="IJraScheduleCollectionWorkflow"/> 等)の
/// フェイクを組み合わせ、ジョブのdequeue→Workflow呼び出し→成功/失敗判定という一連の流れを検証する。
/// </summary>
[TestClass]
public sealed class CollectionExecutionServiceIntegrationTests
{
    private string _stateDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _stateDirectory = Path.Combine(Path.GetTempPath(), "collection-execution-tests", Guid.NewGuid().ToString("N"));
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
    public async Task RaceCardJob_WhenWorkflowSucceeds_CompletesJobAndRecordsSuccessStatus()
    {
        var now = new DateTimeOffset(2026, 5, 16, 3, 0, 0, TimeSpan.Zero);
        var raceDate = new DateOnly(2026, 5, 16);
        var stateStore = CreateStore();

        var scheduleWorkflow = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = _ => new[] { RaceCourse.Tokyo }
        };
        var cardWorkflow = new FakeJraRaceCardCollectionWorkflow
        {
            ResultFactory = (date, course) => new RaceCardCollectionResult(
                date,
                course,
                new[] { "race-1" },
                Array.Empty<string>(),
                new[] { new RaceCardRaceOutcome(1, "race-1", "テストレース", "https://example.test/1", null) })
        };
        var resultWorkflow = new FakeJraRaceResultCollectionWorkflow();

        var service = CreateService(stateStore, scheduleWorkflow, cardWorkflow, resultWorkflow);

        var payload = new RaceCardCollectionJobPayload(raceDate, "JRA");
        var key = AgentJobKeyFactory.BuildRaceCardCollectionKey("JRA", raceDate);
        await stateStore.EnqueueJobAsync(
            AgentJobType.RaceCardCollection,
            key,
            AgentJobPayloadSerializer.Serialize(payload),
            now);

        await service.RunTaskAsync(AgentJobType.RaceCardCollection, CancellationToken.None);

        // 一度だけWorkflowが呼ばれ、ジョブが完了(再取得不可)していることを確認する。
        Assert.HasCount(1, cardWorkflow.Requests);
        Assert.AreEqual((raceDate, RaceCourse.Tokyo), cardWorkflow.Requests[0]);

        var remaining = await stateStore.AcquireReadyJobsAsync(
            AgentJobType.RaceCardCollection, now.AddMinutes(1), TimeSpan.Zero, 10, TimeSpan.FromMinutes(30), CancellationToken.None);
        Assert.IsEmpty(remaining);

        var statuses = await stateStore.GetRaceDataCollectionStatusesAsync(raceDate, raceDate, CancellationToken.None);
        Assert.HasCount(1, statuses);
        Assert.AreEqual(RaceDataCollectionState.Succeeded, statuses[0].RaceCardStatus);
    }

    [TestMethod]
    public async Task RaceCardJob_WhenWorkflowThrows_FailsJobAndKeepsItRetryable()
    {
        var now = new DateTimeOffset(2026, 5, 16, 3, 0, 0, TimeSpan.Zero);
        var raceDate = new DateOnly(2026, 5, 16);
        var stateStore = CreateStore();

        var scheduleWorkflow = new FakeJraScheduleCollectionWorkflow
        {
            ThrowOnCollect = new InvalidOperationException("boom")
        };
        var service = CreateService(
            stateStore,
            scheduleWorkflow,
            new FakeJraRaceCardCollectionWorkflow(),
            new FakeJraRaceResultCollectionWorkflow());

        var payload = new RaceCardCollectionJobPayload(raceDate, "JRA");
        var key = AgentJobKeyFactory.BuildRaceCardCollectionKey("JRA", raceDate);
        await stateStore.EnqueueJobAsync(
            AgentJobType.RaceCardCollection,
            key,
            AgentJobPayloadSerializer.Serialize(payload),
            now);

        await service.RunTaskAsync(AgentJobType.RaceCardCollection, CancellationToken.None);

        // 既存の失敗処理(FailJobAsync)が動作し、後から再取得できる状態になっていることを確認する。
        var statuses = await stateStore.GetJobStatusesAsync(AgentJobType.RaceCardCollection, null, 10, CancellationToken.None);
        Assert.HasCount(1, statuses);
        Assert.AreEqual(AgentJobStatus.Failed, statuses[0].Status);
    }

    [TestMethod]
    public async Task RaceResultJob_WhenOneCourseListFails_OtherCourseIsStillCollectedAndJobCompletes()
    {
        // 依頼1の回帰テスト: 過去月をまたいだ日付でレース一覧取得(ToRaceResultListAsync)が
        // 1競馬場だけ失敗しても、他の競馬場の成績収集は継続し、ジョブ全体は失敗しない
        // （以前は ToRaceListAsync の JraNavigationException がジョブ全体を未捕捉のまま
        // 失敗させていた）。
        var now = new DateTimeOffset(2026, 5, 16, 3, 0, 0, TimeSpan.Zero);
        var raceDate = new DateOnly(2026, 5, 16);
        var stateStore = CreateStore();

        var scheduleWorkflow = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = _ => new[] { RaceCourse.Tokyo, RaceCourse.Nakayama }
        };

        var nakayamaRaceId = new RaceId(raceDate, RaceCourse.Nakayama, 1);
        var nakayamaRaceList = new JraRaceListPage(
            "https://example.jra.go.jp/result-list/nakayama",
            raceDate,
            RaceCourse.Nakayama,
            new[] { new RaceSummary(nakayamaRaceId, "テストレース", new TimeOnly(15, 40), null, null) });

        var sessionFactory = new FakeJraSessionFactory
        {
            ConfigureNavigator = () => new FakeJraNavigator
            {
                RaceResultListFactory = (date, course) => course switch
                {
                    RaceCourse.Tokyo => throw new JraNavigationException(
                        "開催選択ボタンが見つかりませんでした。",
                        JraNavigationFailureReason.OutOfDisplayedRange),
                    RaceCourse.Nakayama => nakayamaRaceList,
                    _ => throw new NotSupportedException(),
                },
            },
        };

        var resultWorkflow = new FakeJraRaceResultCollectionWorkflow
        {
            ResultFactory = raceId => new RaceResultCollectionResult(
                raceId,
                $"race-{raceId.Date:yyyyMMdd}-{raceId.Course}-{raceId.Number}",
                new[] { 1 },
                Array.Empty<string>()),
        };

        var service = CreateService(
            stateStore,
            scheduleWorkflow,
            new FakeJraRaceCardCollectionWorkflow(),
            resultWorkflow,
            sessionFactory);

        var payload = new RaceResultCollectionJobPayload(raceDate, "JRA", AgentWorkMode.Idle);
        var key = AgentJobKeyFactory.BuildRaceResultCollectionKey("JRA", raceDate);
        await stateStore.EnqueueJobAsync(
            AgentJobType.RaceResultCollection,
            key,
            AgentJobPayloadSerializer.Serialize(payload),
            now);

        await service.RunTaskAsync(AgentJobType.RaceResultCollection, CancellationToken.None);

        // 中山(成功)のみ成績収集が行われ、東京(一覧取得失敗)はスキップされている。
        Assert.HasCount(1, resultWorkflow.Requests);
        Assert.AreEqual(nakayamaRaceId, resultWorkflow.Requests[0]);

        // ジョブ全体は失敗せず完了している。
        var statuses = await stateStore.GetJobStatusesAsync(AgentJobType.RaceResultCollection, null, 10, CancellationToken.None);
        Assert.HasCount(1, statuses);
        Assert.AreEqual(AgentJobStatus.Succeeded, statuses[0].Status);
    }

    [TestMethod]
    public async Task RunSingleTaskAsync_WhenLeaseAcquired_ExecutesAndCompletesJob()
    {
        // AcquireCollectionTaskAsync は実際の DateTimeOffset.UtcNow で AvailableAt を判定するため、
        // 未来日を使うとリースを取得できない。実行時の現在日時を基準にする。
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var raceDate = DateOnly.FromDateTime(now.UtcDateTime);
        var stateStore = CreateStore();

        var scheduleWorkflow = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = _ => new[] { RaceCourse.Tokyo }
        };
        var cardWorkflow = new FakeJraRaceCardCollectionWorkflow
        {
            ResultFactory = (date, course) => new RaceCardCollectionResult(
                date,
                course,
                new[] { "race-1" },
                Array.Empty<string>(),
                new[] { new RaceCardRaceOutcome(1, "race-1", "テストレース", "https://example.test/1", null) })
        };
        var service = CreateService(stateStore, scheduleWorkflow, cardWorkflow, new FakeJraRaceResultCollectionWorkflow());

        var payload = new RaceCardCollectionJobPayload(raceDate, "JRA");
        var key = AgentJobKeyFactory.BuildRaceCardCollectionKey("JRA", raceDate);
        await stateStore.EnqueueJobAsync(AgentJobType.RaceCardCollection, key, AgentJobPayloadSerializer.Serialize(payload), now);

        // EnqueueJobAsync は内部でQueueDispatchを呼ぶため、新規ジョブのDispatchGenerationは
        // 初期値0ではなく1になる（実際にSQSへ送出される通知もこの値を持つ）。
        var notification = new CollectionTaskNotification(key, AgentJobType.RaceCardCollection, key, DispatchGeneration: 1);
        var handled = await service.RunSingleTaskAsync(notification, CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.HasCount(1, cardWorkflow.Requests);

        var statuses = await stateStore.GetJobStatusesAsync(AgentJobType.RaceCardCollection, null, 10, CancellationToken.None);
        Assert.HasCount(1, statuses);
        Assert.AreEqual(AgentJobStatus.Succeeded, statuses[0].Status);
    }

    [TestMethod]
    public async Task RunSingleTaskAsync_WhenWorkflowThrows_AppendsRequestIdToErrorMessage()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var raceDate = DateOnly.FromDateTime(now.UtcDateTime);
        var stateStore = CreateStore();

        var scheduleWorkflow = new FakeJraScheduleCollectionWorkflow
        {
            ThrowOnCollect = new InvalidOperationException("boom")
        };
        var service = CreateService(
            stateStore, scheduleWorkflow, new FakeJraRaceCardCollectionWorkflow(), new FakeJraRaceResultCollectionWorkflow());

        var payload = new RaceCardCollectionJobPayload(raceDate, "JRA");
        var key = AgentJobKeyFactory.BuildRaceCardCollectionKey("JRA", raceDate);
        await stateStore.EnqueueJobAsync(AgentJobType.RaceCardCollection, key, AgentJobPayloadSerializer.Serialize(payload), now);

        var notification = new CollectionTaskNotification(key, AgentJobType.RaceCardCollection, key, DispatchGeneration: 1);
        await service.RunSingleTaskAsync(notification, "test-request-id-123", CancellationToken.None);

        var statuses = await stateStore.GetJobStatusesAsync(AgentJobType.RaceCardCollection, null, 10, CancellationToken.None);
        Assert.HasCount(1, statuses);
        Assert.AreEqual(AgentJobStatus.Failed, statuses[0].Status);
        var detail = await stateStore.GetJobDetailAsync(statuses[0].JobId, CancellationToken.None);
        StringAssert.Contains(detail!.LastError, "test-request-id-123");
    }

    [TestMethod]
    public async Task RunSingleTaskAsync_WhenJobAlreadyRunning_ReturnsFalseWithoutExecuting()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var raceDate = DateOnly.FromDateTime(now.UtcDateTime);
        var stateStore = CreateStore();

        var cardWorkflow = new FakeJraRaceCardCollectionWorkflow();
        var service = CreateService(
            stateStore, new FakeJraScheduleCollectionWorkflow(), cardWorkflow, new FakeJraRaceResultCollectionWorkflow());

        var payload = new RaceCardCollectionJobPayload(raceDate, "JRA");
        var key = AgentJobKeyFactory.BuildRaceCardCollectionKey("JRA", raceDate);
        await stateStore.EnqueueJobAsync(AgentJobType.RaceCardCollection, key, AgentJobPayloadSerializer.Serialize(payload), now);
        // 別プロセスが既にリースを取得済み（Running）の状態を再現する。
        await stateStore.AcquireReadyJobsAsync(
            AgentJobType.RaceCardCollection, now.AddMinutes(1), TimeSpan.Zero, 10, TimeSpan.FromMinutes(30), CancellationToken.None);

        var notification = new CollectionTaskNotification(key, AgentJobType.RaceCardCollection, key, DispatchGeneration: 1);
        var handled = await service.RunSingleTaskAsync(notification, CancellationToken.None);

        // リースが取得できないため、何も実行せず即終了する。
        Assert.IsFalse(handled);
        Assert.IsEmpty(cardWorkflow.Requests);
    }

    [TestMethod]
    public async Task RunSingleTaskAsync_WhenCancelledByInternalDeadline_RequeuesInsteadOfFailing()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var raceDate = DateOnly.FromDateTime(now.UtcDateTime);
        var stateStore = CreateStore();

        using var cts = new CancellationTokenSource();
        var scheduleWorkflow = new FakeJraScheduleCollectionWorkflow
        {
            // Workflow呼び出し時点で外側のキャンセルが既に発生している状況を再現する
            // （14分の内部デッドラインが処理途中で到達したケースに相当）。
            CoursesByDate = _ =>
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
                return Array.Empty<RaceCourse>();
            }
        };
        var service = CreateService(
            stateStore, scheduleWorkflow, new FakeJraRaceCardCollectionWorkflow(), new FakeJraRaceResultCollectionWorkflow());

        var payload = new RaceCardCollectionJobPayload(raceDate, "JRA");
        var key = AgentJobKeyFactory.BuildRaceCardCollectionKey("JRA", raceDate);
        await stateStore.EnqueueJobAsync(AgentJobType.RaceCardCollection, key, AgentJobPayloadSerializer.Serialize(payload), now);

        var notification = new CollectionTaskNotification(key, AgentJobType.RaceCardCollection, key, DispatchGeneration: 1);
        var handled = await service.RunSingleTaskAsync(notification, cts.Token);

        // 恒久的な失敗（Failed）ではなく、Readyへ戻され再試行可能になっている。
        Assert.IsTrue(handled);
        var statuses = await stateStore.GetJobStatusesAsync(AgentJobType.RaceCardCollection, null, 10, CancellationToken.None);
        Assert.HasCount(1, statuses);
        Assert.AreEqual(AgentJobStatus.Ready, statuses[0].Status);
    }

    private static CollectionExecutionService CreateService(
        ProcessingStateStore stateStore,
        IJraScheduleCollectionWorkflow scheduleWorkflow,
        IJraRaceCardCollectionWorkflow cardWorkflow,
        IJraRaceResultCollectionWorkflow resultWorkflow,
        FakeJraSessionFactory? sessionFactory = null)
    {
        sessionFactory ??= new FakeJraSessionFactory();
        var options = Options.Create(new AgentProcessingOptions
        {
            CollectionBatchSize = 10,
            CollectionLeaseMinutes = 30,
        });

        var planner = new HistoricalDataRequestPlanner(
            new NullRaceQueryService(),
            stateStore,
            new NullHistoricalRaceReferenceCollector(),
            NullLogger<HistoricalDataRequestPlanner>.Instance);

        return new CollectionExecutionService(
            options,
            stateStore,
            sessionFactory,
            _ => scheduleWorkflow,
            _ => cardWorkflow,
            _ => resultWorkflow,
            planner,
            new CollectionExecutionTrigger(),
            NullLogger<CollectionExecutionService>.Instance);
    }

    private ProcessingStateStore CreateStore()
    {
        var options = Options.Create(new AgentProcessingOptions
        {
            StateDirectory = _stateDirectory,
            PredictionLeaseMinutes = 5,
            MaxConcurrentJobs = 10,
        });

        return new ProcessingStateStore(options, NullLogger<ProcessingStateStore>.Instance);
    }
}
