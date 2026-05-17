using HorseRacingPrediction.AgentClient.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Tests.Scheduling;

[TestClass]
public sealed class HistoricalDataRequestTrackerTests
{
    private string _stateDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _stateDirectory = Path.Combine(Path.GetTempPath(), "historical-request-tracker-tests", Guid.NewGuid().ToString("N"));
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
    public async Task GetOutstandingRequestsAsync_CountsOnlyMatchingRaceRequests()
    {
        var store = CreateStore();
        var now = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);

        await store.ScheduleJobAsync(
            AgentJobType.HistoricalRaceResultCollectionRequest,
            "race-result-1",
            AgentJobPayloadSerializer.Serialize(new HistoricalRaceResultCollectionRequestPayload(new DateOnly(2026, 4, 13), "中山", 11, "race-1", "JRA")),
            now);
        await store.ScheduleJobAsync(
            AgentJobType.HistoricalRaceResultCollectionRequest,
            "race-result-2",
            AgentJobPayloadSerializer.Serialize(new HistoricalRaceResultCollectionRequestPayload(new DateOnly(2026, 3, 2), "中山", 9, "race-1", "JRA")),
            now);
        await store.ScheduleJobAsync(
            AgentJobType.HistoricalRaceResultCollectionRequest,
            "race-result-3",
            AgentJobPayloadSerializer.Serialize(new HistoricalRaceResultCollectionRequestPayload(new DateOnly(2026, 2, 8), "東京", 10, "race-2", "JRA")),
            now);

        var tracker = new HistoricalDataRequestTracker(store);
        var summary = await tracker.GetOutstandingRequestsAsync("race-1");

        Assert.AreEqual(0, summary.PendingHorseRequests);
        Assert.AreEqual(0, summary.PendingJockeyRequests);
        Assert.AreEqual(2, summary.PendingRaceResultRequests);
        Assert.AreEqual(2, summary.TotalPendingRequests);
    }

    private ProcessingStateStore CreateStore()
    {
        var options = Options.Create(new AgentProcessingOptions
        {
            StateDirectory = _stateDirectory,
            PredictionLeaseMinutes = 5,
            CollectionLeaseMinutes = 5
        });

        return new ProcessingStateStore(options, NullLogger<ProcessingStateStore>.Instance);
    }
}