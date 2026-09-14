using HorseRacingPrediction.CollectionInitializer;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionPlatformInitializationTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "collection-initializer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task DryRunDoesNotWrite_ExecuteIsIdempotent_AndLegacyJobsAreIgnored()
    {
        var domainPath = Path.Combine(_directory, "domain.db");
        await CreateDomainFixtureAsync(domainPath);
        var seeds = await new DomainCollectionSeedReader(domainPath).ReadAsync(
            new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero));
        Assert.HasCount(3, seeds); // unified race detail + cited horse/jockey; legacy_jobs is deliberately ignored
        var store = await CreateStoreAsync();

        var preview = await store.InitializeFromDomainDataAsync(seeds, dryRun: true);
        Assert.AreEqual(3, preview.ResourcesAdded);
        Assert.AreEqual(3, preview.StatesAdded);
        Assert.AreEqual(2, preview.LocationsAdded);
        Assert.IsNull(await store.GetStateAsync(new(ResourceType.RaceCard, "JRA", "race-1"), new("race-card")));

        var applied = await store.InitializeFromDomainDataAsync(seeds, dryRun: false);
        Assert.AreEqual(3, applied.ResourcesAdded);
        Assert.AreEqual(3, applied.StatesAdded);
        Assert.AreEqual(2, applied.LocationsAdded);
        Assert.AreEqual(CollectionStateStatus.Current,
            (await store.GetStateAsync(new(ResourceType.Race, "JRA", "20240106:Tokyo:1"), new("race-detail")))!.Status);
        Assert.HasCount(1, await store.ResolveLocationsAsync(new(ResourceType.Horse, "JRA", "horse-1"),
            new("horse-profile")));
        Assert.HasCount(1, await store.ResolveLocationsAsync(new(ResourceType.Jockey, "JRA", "jockey-1"),
            new("jockey-profile")));

        var repeated = await store.InitializeFromDomainDataAsync(seeds, dryRun: false);
        Assert.AreEqual(0, repeated.ResourcesAdded);
        Assert.AreEqual(0, repeated.StatesAdded);
        Assert.AreEqual(0, repeated.LocationsAdded);
        Assert.IsEmpty(await store.GetTasksAsync());
    }

    [TestMethod]
    public async Task CliDryRun_DoesNotCreateTargetDirectoryOrDatabase()
    {
        var domainPath = Path.Combine(_directory, "domain.db");
        var target = Path.Combine(_directory, "absent-state");
        await CreateDomainFixtureAsync(domainPath);

        var exitCode = await CollectionInitializerCommand.RunAsync(
            ["--domain-db", domainPath, "--state-dir", target], new StringWriter(), new StringWriter());

        Assert.AreEqual(0, exitCode);
        Assert.IsFalse(Directory.Exists(target));
        Assert.IsFalse(File.Exists(Path.Combine(target, "collection-platform.db")));
    }

    [TestMethod]
    public async Task CliExecute_RegistersAllSubjectDefinitions()
    {
        var domainPath = Path.Combine(_directory, "domain.db");
        var target = Path.Combine(_directory, "state");
        await CreateDomainFixtureAsync(domainPath);

        Assert.AreEqual(0, await CollectionInitializerCommand.RunAsync(
            ["--domain-db", domainPath, "--state-dir", target, "--execute"],
            new StringWriter(), new StringWriter()));

        var databasePath = Path.Combine(target, "collection-platform.db");
        Assert.AreEqual(4, await ReadScalarAsync(databasePath,
            "SELECT COUNT(*) FROM collection_definitions WHERE DefinitionId IN ('horse-profile','jockey-profile','trainer-profile','owner-identity');"));
    }

    [TestMethod]
    public async Task IncompleteRaceSeed_IsScheduledForUnifiedSupplement()
    {
        var store = await CreateStoreAsync();
        var importedAt = new DateTimeOffset(2026, 9, 14, 1, 0, 0, TimeSpan.Zero);
        var resource = new ResourceKey(ResourceType.Race, "JRA", "20260914:Nakayama:1");
        await store.InitializeFromDomainDataAsync([
            new(resource, new("race-detail"), 1, importedAt, new DateOnly(2026, 9, 14),
                new Dictionary<string, string> { ["course"] = "中山", ["number"] = "1" },
                IsComplete: false)
        ], dryRun: false);

        var state = await store.GetStateAsync(resource, new("race-detail"));
        Assert.IsNotNull(state);
        Assert.AreEqual(CollectionStateStatus.RefreshDue, state.Status);
        Assert.AreEqual(0, state.AppliedRevision);
        Assert.AreEqual(importedAt, state.NextCollectionAt);
    }

    [TestMethod]
    public async Task CliDryRun_DoesNotChangeExistingTargetDatabaseBytesOrContent()
    {
        var domainPath = Path.Combine(_directory, "domain.db");
        var target = Path.Combine(_directory, "existing-state");
        await CreateDomainFixtureAsync(domainPath);
        Assert.AreEqual(0, await CollectionInitializerCommand.RunAsync(
            ["--domain-db", domainPath, "--state-dir", target, "--execute"],
            new StringWriter(), new StringWriter()));
        var databasePath = Path.Combine(target, "collection-platform.db");
        var beforeHash = SHA256.HashData(await File.ReadAllBytesAsync(databasePath));
        var beforeState = await ReadScalarAsync(databasePath, "SELECT COUNT(*) FROM collection_states;");

        Assert.AreEqual(0, await CollectionInitializerCommand.RunAsync(
            ["--domain-db", domainPath, "--state-dir", target], new StringWriter(), new StringWriter()));

        CollectionAssert.AreEqual(beforeHash, SHA256.HashData(await File.ReadAllBytesAsync(databasePath)));
        Assert.AreEqual(beforeState, await ReadScalarAsync(databasePath, "SELECT COUNT(*) FROM collection_states;"));
    }

    private async Task<CollectionPlatformStore> CreateStoreAsync()
    {
        var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
        {
            StateDirectory = Path.Combine(_directory, "state")
        }));
        await store.RegisterDefinitionAsync(new("race-detail"), "Race detail", ResourceType.Race, 1, "initial", false);
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", ResourceType.Horse, 1, "initial", false);
        await store.RegisterDefinitionAsync(new("jockey-profile"), "Jockey profile", ResourceType.Jockey, 1, "initial", false);
        await store.RegisterDefinitionAsync(new("trainer-profile"), "Trainer profile", ResourceType.Trainer, 1, "initial", false);
        return store;
    }

    private static async Task CreateDomainFixtureAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE RaceSummaries (RaceId TEXT, RaceDate TEXT, RacecourseCode TEXT, RaceNumber INTEGER,
                EntryCount INTEGER, ResultDeclaredAt TEXT);
            CREATE TABLE Horses (HorseId TEXT);
            CREATE TABLE Jockeys (JockeyId TEXT);
            CREATE TABLE Trainers (TrainerId TEXT);
            CREATE TABLE JraSubjectProfileReadModel (SubjectId TEXT, SourceUrl TEXT, AcquiredAt TEXT);
            CREATE TABLE legacy_jobs (JobId TEXT, Url TEXT);
            INSERT INTO RaceSummaries VALUES ('race-1','2024-01-06','東京',1,16,'2024-01-06T07:00:00+00:00');
            INSERT INTO Horses VALUES ('horse-1');
            INSERT INTO Jockeys VALUES ('jockey-1');
            INSERT INTO JraSubjectProfileReadModel VALUES ('horse-1','https://example.test/horse/1','2024-01-06T01:00:00+00:00');
            INSERT INTO JraSubjectProfileReadModel VALUES ('jockey-1','https://example.test/jockey/1','2024-01-06T01:30:00+00:00');
            INSERT INTO legacy_jobs VALUES ('old-job','https://should-not-be-imported.test/');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ReadScalarAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
