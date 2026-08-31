using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.Scheduling;

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
            "RaceCardCollection:20260517-tokyo-11",
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
        Assert.AreEqual("RaceCardCollection:20260517-tokyo-11", result[2].OriginJobId);
    }

    [TestMethod]
    public async Task Constructor_CreatesAgentAcquisitionTableForExistingDatabase()
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

        var store = CreateStore();
        var now = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        await store.UpsertAgentAcquisitionStatusAsync(
            "Horse:EntityUpsert:既存DBテスト",
            AgentAcquisitionSubjectType.Horse,
            AgentAcquisitionOperationType.EntityUpsert,
            "API",
            "horse-existing-db",
            "既存DBテスト",
            null,
            "RaceCardCollection:existing-db",
            null,
            RaceDataCollectionState.Succeeded,
            null,
            null,
            now);

        var result = await store.GetAgentAcquisitionStatusesAsync(
            new DateOnly(2026, 8, 30),
            new DateOnly(2026, 8, 30),
            null,
            null);

        Assert.HasCount(1, result);
        Assert.AreEqual("RaceCardCollection:existing-db", result[0].OriginJobId);
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
