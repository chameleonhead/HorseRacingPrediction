using HorseRacingPrediction.CollectionCutover;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class LegacyJobDatabaseCutoverCommandTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "collection-cutover-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task DryRun_IsDefaultAndDoesNotChangeAnyFile()
    {
        var legacy = Path.Combine(_directory, "collection-tasks.db");
        await File.WriteAllTextAsync(legacy, "legacy-data");

        Assert.AreEqual(0, await RunAsync(["--state-dir", _directory]));

        Assert.AreEqual("legacy-data", await File.ReadAllTextAsync(legacy));
        Assert.IsFalse(Directory.Exists(Path.Combine(_directory, "cutover-backups")));
    }

    [TestMethod]
    public async Task Execute_RequiresSuccessfulSmoke_BacksUpOnlyLegacyDbAndSidecars_AndIsIdempotent()
    {
        var smokeTaskId = Guid.NewGuid();
        await CreateSmokeDatabaseAsync(Path.Combine(_directory, "collection-platform.db"), smokeTaskId, "Succeeded");
        var protectedFiles = new Dictionary<string, string>
        {
            ["eventstore.db"] = "domain",
            ["prediction-executions.db"] = "predictions",
            ["source-citations.json"] = "citations",
        };
        foreach (var item in protectedFiles) await File.WriteAllTextAsync(Path.Combine(_directory, item.Key), item.Value);
        foreach (var name in new[] { "collection-tasks.db", "collection-tasks.db-wal", "collection-tasks.db-shm" })
            await File.WriteAllTextAsync(Path.Combine(_directory, name), name);

        Assert.AreEqual(0, await RunAsync(["--state-dir", _directory, "--execute",
            "--confirm-delete-legacy-job-db", "--smoke-task-id", smokeTaskId.ToString()]));

        foreach (var name in new[] { "collection-tasks.db", "collection-tasks.db-wal", "collection-tasks.db-shm" })
            Assert.IsFalse(File.Exists(Path.Combine(_directory, name)));
        var backup = Directory.GetDirectories(Path.Combine(_directory, "cutover-backups")).Single();
        Assert.HasCount(3, Directory.GetFiles(backup));
        foreach (var item in protectedFiles)
            Assert.AreEqual(item.Value, await File.ReadAllTextAsync(Path.Combine(_directory, item.Key)));
        Assert.IsTrue(File.Exists(Path.Combine(_directory, "collection-platform.db")));

        Assert.AreEqual(0, await RunAsync(["--state-dir", _directory, "--execute",
            "--confirm-delete-legacy-job-db", "--smoke-task-id", smokeTaskId.ToString()]));
        Assert.HasCount(1, Directory.GetDirectories(Path.Combine(_directory, "cutover-backups")));
    }

    [TestMethod]
    public async Task Execute_RejectsMissingOrUnsuccessfulSmokeWithoutDeletingLegacyDb()
    {
        var legacy = Path.Combine(_directory, "collection-tasks.db");
        await File.WriteAllTextAsync(legacy, "legacy-data");
        var smokeTaskId = Guid.NewGuid();
        await CreateSmokeDatabaseAsync(Path.Combine(_directory, "collection-platform.db"), smokeTaskId, "Failed");

        Assert.AreEqual(2, await RunAsync(["--state-dir", _directory, "--execute"]));
        Assert.AreEqual(3, await RunAsync(["--state-dir", _directory, "--execute",
            "--confirm-delete-legacy-job-db", "--smoke-task-id", smokeTaskId.ToString()]));
        Assert.AreEqual("legacy-data", await File.ReadAllTextAsync(legacy));
    }

    [TestMethod]
    public async Task FilesystemRoot_IsRejectedOnTheCurrentOperatingSystem()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(_directory));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => RunAsync(["--state-dir", root!]));
    }

    private static async Task<int> RunAsync(string[] args) => await LegacyJobDatabaseCutoverCommand.RunAsync(
        args, new StringWriter(), new StringWriter());

    private static async Task CreateSmokeDatabaseAsync(string path, Guid taskId, string status)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE collection_tasks (TaskId TEXT PRIMARY KEY, Status TEXT NOT NULL); " +
                              "INSERT INTO collection_tasks VALUES ($taskId, $status);";
        command.Parameters.AddWithValue("$taskId", taskId.ToString());
        command.Parameters.AddWithValue("$status", status);
        await command.ExecuteNonQueryAsync();
    }
}
