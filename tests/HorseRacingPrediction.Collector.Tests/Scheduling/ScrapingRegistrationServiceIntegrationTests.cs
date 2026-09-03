using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Collector.Tests.TestSupport;
using HorseRacingPrediction.Scraping.Jra.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

/// <summary>
/// <see cref="ScrapingRegistrationService"/> の統合テスト。実DB(SQLite)の
/// <see cref="ProcessingStateStore"/> と <see cref="IJraScheduleCollectionWorkflow"/> のフェイクを
/// 組み合わせ、「開催競馬場ごとにジョブが登録される」「開催なしなら何も登録しない」ことを検証する。
/// </summary>
[TestClass]
public sealed class ScrapingRegistrationServiceIntegrationTests
{
    private string _stateDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _stateDirectory = Path.Combine(Path.GetTempPath(), "scraping-registration-tests", Guid.NewGuid().ToString("N"));
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
    public async Task RunOneCycleAsync_WhenScheduleHasRaceDays_RegistersRaceCardJobForEachDay()
    {
        var stateStore = CreateStore();
        // RunOneCycleAsyncは実時刻(JST変換後の今日)を基準にするため、テストも実行時の「今日」に合わせる。
        var jst = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, jst).Date);
        var scheduleWorkflow = new FakeJraScheduleCollectionWorkflow
        {
            // ScheduleLookaheadDaysを0にして基準日のみを対象にする。
            CoursesByDate = date => date == today
                ? new[] { RaceCourse.Tokyo, RaceCourse.Kyoto }
                : Array.Empty<RaceCourse>()
        };

        var service = CreateService(stateStore, scheduleWorkflow, scheduleLookaheadDays: 0);

        await service.RunOneCycleAsync(CancellationToken.None);

        var jobs = await stateStore.AcquireReadyJobsAsync(
            AgentJobType.RaceCardCollection,
            DateTimeOffset.UtcNow.AddDays(1),
            TimeSpan.Zero,
            10,
            TimeSpan.FromMinutes(30),
            CancellationToken.None);

        // 開催の有無はCourseごとではなく日単位で判定されるため、開催日1日につきジョブは1件登録される。
        Assert.AreEqual(1, jobs.Count);
        var payload = AgentJobPayloadSerializer.Deserialize<RaceCardCollectionJobPayload>(jobs[0].Payload);
        Assert.AreEqual(today, payload.RaceDate);
    }

    [TestMethod]
    public async Task RunOneCycleAsync_WhenNoRaceDayInLookaheadWindow_RegistersNoJobs()
    {
        var stateStore = CreateStore();
        var scheduleWorkflow = new FakeJraScheduleCollectionWorkflow
        {
            CoursesByDate = _ => Array.Empty<RaceCourse>()
        };

        var service = CreateService(stateStore, scheduleWorkflow, scheduleLookaheadDays: 0);

        await service.RunOneCycleAsync(CancellationToken.None);

        var jobs = await stateStore.AcquireReadyJobsAsync(
            AgentJobType.RaceCardCollection,
            DateTimeOffset.UtcNow.AddDays(1),
            TimeSpan.Zero,
            10,
            TimeSpan.FromMinutes(30),
            CancellationToken.None);

        Assert.AreEqual(0, jobs.Count);
    }

    private static ScrapingRegistrationService CreateService(
        ProcessingStateStore stateStore,
        FakeJraScheduleCollectionWorkflow scheduleWorkflow,
        int scheduleLookaheadDays)
    {
        var sessionFactory = new FakeJraSessionFactory();
        var options = Options.Create(new AgentProcessingOptions
        {
            EnableScheduleCollection = true,
            EnableRaceCardCollection = true,
            ScheduleLookaheadDays = scheduleLookaheadDays,
        });

        return new ScrapingRegistrationService(
            options,
            sessionFactory,
            _ => scheduleWorkflow,
            stateStore,
            new CollectionExecutionTrigger(),
            NullLogger<ScrapingRegistrationService>.Instance);
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
