using HorseRacingPrediction.AgentClient.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Tests.Scheduling;

[TestClass]
public sealed class AgentAcquisitionStatusStoreTests
{
    private string _stateDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _stateDirectory = Path.Combine(Path.GetTempPath(), "agent-acquisition-status-store-tests", Guid.NewGuid().ToString("N"));
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
    public async Task GetAgentAcquisitionStatusesAsync_ReturnsHorseJockeyAndTrainerStatuses()
    {
        var store = CreateStore();
        var now = new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero);

        await store.UpsertAgentAcquisitionStatusAsync(
            "Horse:EntityUpsert:ソールオリエンス",
            AgentAcquisitionSubjectType.Horse,
            AgentAcquisitionOperationType.EntityUpsert,
            "API",
            "horse-1",
            "ソールオリエンス",
            null,
            null,
            RaceDataCollectionState.Succeeded,
            null,
            null,
            now);
        await store.UpsertAgentAcquisitionStatusAsync(
            "Jockey:EntityUpsert:横山武史",
            AgentAcquisitionSubjectType.Jockey,
            AgentAcquisitionOperationType.EntityUpsert,
            "API",
            "jockey-1",
            "横山武史",
            null,
            null,
            RaceDataCollectionState.Failed,
            RaceDataCollectionErrorCode.ExternalRequestFailed,
            "remote request failed",
            now.AddMinutes(1));
        await store.UpsertAgentAcquisitionStatusAsync(
            "Trainer:EntityUpsert:手塚貴久",
            AgentAcquisitionSubjectType.Trainer,
            AgentAcquisitionOperationType.EntityUpsert,
            "API",
            "trainer-1",
            "手塚貴久",
            null,
            null,
            RaceDataCollectionState.Succeeded,
            null,
            null,
            now.AddMinutes(2));

        var result = await store.GetAgentAcquisitionStatusesAsync(
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            subjectType: null,
            status: null);

        Assert.HasCount(3, result);
        Assert.AreEqual(AgentAcquisitionSubjectType.Trainer, result[0].SubjectType);
        Assert.AreEqual(AgentAcquisitionSubjectType.Jockey, result[1].SubjectType);
        Assert.AreEqual(RaceDataCollectionErrorCode.ExternalRequestFailed, result[1].ErrorCode);
        Assert.AreEqual(AgentAcquisitionSubjectType.Horse, result[2].SubjectType);
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