using HorseRacingPrediction.AgentClient.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Tests.Scheduling;

[TestClass]
public sealed class RaceDataCollectionStatusStoreTests
{
    private string _stateDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _stateDirectory = Path.Combine(Path.GetTempPath(), "race-data-collection-status-store-tests", Guid.NewGuid().ToString("N"));
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
    public async Task GetRaceDataCollectionStatusesAsync_ReturnsMergedCardAndResultStatus()
    {
        var store = CreateStore();
        var now = new DateTimeOffset(2026, 5, 17, 10, 0, 0, TimeSpan.Zero);

        await store.UpsertRaceCardCollectionStatusAsync(
            new DateOnly(2026, 5, 17),
            "東京",
            11,
            "race-1",
            "オークス",
            "https://example.test/card",
            RaceDataCollectionState.Succeeded,
            errorCode: null,
            errorReason: null,
            now);
        await store.UpsertRaceResultCollectionStatusAsync(
            new DateOnly(2026, 5, 17),
            "東京",
            11,
            "race-1",
            "オークス",
            "https://example.test/result",
            RaceDataCollectionState.Failed,
            RaceResultAcquisitionOrigin.HistoricalDependency,
            "race-target",
            RaceDataCollectionErrorCode.NavigationFailed,
            "ページを開けませんでした。",
            now.AddMinutes(5));

        var result = await store.GetRaceDataCollectionStatusesAsync(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

        Assert.HasCount(1, result);
        Assert.AreEqual("オークス", result[0].RaceName);
        Assert.AreEqual(RaceDataCollectionState.Succeeded, result[0].RaceCardStatus);
        Assert.AreEqual(RaceDataCollectionState.Failed, result[0].RaceResultStatus);
        Assert.AreEqual(RaceDataCollectionErrorCode.NavigationFailed, result[0].RaceResultErrorCode);
        Assert.AreEqual("race-target", result[0].RequestedByRaceId);
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