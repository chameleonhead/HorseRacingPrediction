using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

[TestClass]
public sealed class ResultDayCollectionStatusStoreTests
{
    private string _stateDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _stateDirectory = Path.Combine(Path.GetTempPath(), "result-day-status-store-tests", Guid.NewGuid().ToString("N"));
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
    public async Task UpsertResultDayCollectionStatusAsync_PersistsAndReadsBackStatus()
    {
        var store = CreateStore();
        var now = new DateTimeOffset(2026, 5, 17, 10, 0, 0, TimeSpan.Zero);
        var date = new DateOnly(2026, 5, 17);

        await store.UpsertResultDayCollectionStatusAsync(
            "JRA",
            date,
            ResultDayCollectionState.RetryScheduled,
            expectedRaceCount: 24,
            completedRaceCount: 18,
            incompleteReason: "一部レース未確定",
            lastCompletedAt: null,
            retryAfter: now.AddHours(2),
            lastError: "race result unavailable",
            now);

        var status = await store.GetResultDayCollectionStatusAsync("JRA", date);

        Assert.IsNotNull(status);
        Assert.AreEqual(ResultDayCollectionState.RetryScheduled, status.Status);
        Assert.AreEqual(24, status.ExpectedRaceCount);
        Assert.AreEqual(18, status.CompletedRaceCount);
        Assert.AreEqual("一部レース未確定", status.IncompleteReason);
    }

    private ProcessingStateStore CreateStore()
    {
        var options = Options.Create(new AgentProcessingOptions
        {
            StateDirectory = _stateDirectory,
            PredictionLeaseMinutes = 5,
            CollectionLeaseMinutes = 5,
        });

        return new ProcessingStateStore(options, NullLogger<ProcessingStateStore>.Instance);
    }
}