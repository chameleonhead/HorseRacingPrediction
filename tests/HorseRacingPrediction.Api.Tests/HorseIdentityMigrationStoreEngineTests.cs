using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.PredictionScheduling;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationStoreEngineTests
{
    private const string OldHorseId = "horse-11111111-1111-4111-8111-111111111111";
    private const string JraIdentity = "jra-horse:2024100828";

    [TestMethod]
    public async Task ValidManifestReadsConfiguredSchemasAndRootsButNeverClaimsWriteReadiness()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        var before = await fixture.SourceSnapshotAsync();

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.Incomplete, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.TypedReferenceClosurePending, result.Reason,
            string.Join(",", result.Stores.Select(store => $"{store.Surface}:{store.Status}:{store.UnknownObjectCount}")));
        Assert.IsFalse(result.CanWrite, "T7a has not inspected all typed references and cannot authorize a writer.");
        Assert.IsFalse(result.Stores.Any(store => store.Status is MigrationStoreReadStatus.Unavailable
            or MigrationStoreReadStatus.UnknownShape or MigrationStoreReadStatus.Unsupported));
        Assert.AreEqual(4, result.Stores.Count);
        Assert.AreEqual(MigrationStoreReadStatus.NotConfigured,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.LocalCollectionQueue).Status);
        Assert.AreEqual(MigrationRootStatus.Present, result.Roots.Single().SourceStatus);
        Assert.IsFalse(result.Roots.Single().HasTargetCollision);
        Assert.AreEqual(before, await fixture.SourceSnapshotAsync(), "The read-only scan must not mutate source stores.");
    }

    [TestMethod]
    public async Task ExistingCanonicalHorseRootOrStreamIsReportedAsTargetCollision()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        var destination = DestinationHorseId();
        await fixture.SeedHorseAsync(destination);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.DestinationCollision, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.DestinationCollision, result.Reason);
        Assert.IsFalse(result.CanWrite);
        var root = result.Roots.Single();
        Assert.AreEqual(MigrationRootStatus.TargetCollision, root.SourceStatus);
        Assert.IsTrue(root.TargetHorseRootExists);
        Assert.IsTrue(root.TargetEventStreamExists);
    }

    [TestMethod]
    public async Task ExistingCanonicalCollectionResourceNaturalKeyBlocksTheMapping()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedJraResourceAsync(OldHorseId);
        await fixture.SeedJraResourceAsync(DestinationHorseId());

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.DestinationCollision, result.Status);
        var root = result.Roots.Single();
        Assert.IsTrue(root.TargetCollectionResourceExists);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task TargetCollectionResourceWithoutSourceResourceIsNotNaturalKeyCollision()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.SeedJraResourceAsync(DestinationHorseId());

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.Incomplete, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.TypedReferenceClosurePending, result.Reason);
        Assert.IsTrue(result.Roots.Single().TargetCollectionResourceExists);
        Assert.IsFalse(result.Roots.Single().HasTargetCollision);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task ExistingCanonicalFailureHistoryDoesNotBlockAsNaturalKeyCollision()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        var target = DestinationHorseId();
        await fixture.ExecuteAsync(fixture.CollectionPath, $"""
            INSERT INTO collection_failure_notifications
                (NotificationId, TaskId, Status, AttemptCount, FailedAt, AvailableAt,
                 PublishAttemptCount, ResolutionStatus, CanonicalResourceType,
                 CanonicalResourceProvider, CanonicalResourceId)
            VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', 'Failed', 1,
                    '2026-10-14 00:00:00', '2026-10-14 00:00:00', 0, 'Open',
                    'Horse', 'JRA', '{target}');
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.Incomplete, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.TypedReferenceClosurePending, result.Reason);
        Assert.IsTrue(result.Roots.Single().TargetFailureNotificationExists);
        Assert.IsFalse(result.Roots.Single().HasTargetCollision);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownTableOrCriticalColumnShapeStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, "CREATE TABLE unexpected_identity_table (id TEXT PRIMARY KEY);");

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.Incomplete, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.IsTrue(result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).UnknownObjectCount > 0);
        Assert.AreEqual(0, result.Roots.Count, "Unknown schema must prevent source/target row interpretation.");
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownColumnStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath,
            "ALTER TABLE Horses ADD COLUMN UnexpectedIdentity TEXT NULL;");

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownIndexStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath,
            "CREATE INDEX unexpected_identity_index ON Horses (NormalizedName);");

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task SqlitePrefixLookalikeObjectsStopBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, """
            CREATE TABLE sqliteX_identity_table (id TEXT PRIMARY KEY);
            CREATE INDEX sqliteX_identity_index ON sqliteX_identity_table (id);
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task ChangedFilteredIndexPredicateStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath, """
            DROP INDEX IX_race_repair_holds_RaceId;
            CREATE UNIQUE INDEX IX_race_repair_holds_RaceId
                ON race_repair_holds (RaceId) WHERE ReleasedAt IS NOT NULL;
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task NullableTextPrimaryKeyStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.EventStorePath, """
            ALTER TABLE Horses RENAME TO Horses_original;
            CREATE TABLE Horses (
                HorseId TEXT PRIMARY KEY,
                RegisteredName TEXT NOT NULL,
                NormalizedName TEXT NOT NULL,
                SexCode TEXT NULL,
                BirthDate TEXT NULL,
                OwnerName TEXT NULL,
                BreederName TEXT NULL,
                SireName TEXT NULL,
                DamName TEXT NULL,
                DamsireName TEXT NULL,
                CoatColor TEXT NULL,
                Aliases TEXT NOT NULL
            );
            DROP TABLE Horses_original;
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownCollectionHistoryVersionStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.CollectionPath,
            "INSERT INTO collection_schema_history (version, applied_at) VALUES (26, 'future');");

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownScheduleColumnTypeStopsBeforeRootReads()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        await fixture.ExecuteAsync(fixture.SchedulePath, """
            DROP INDEX IX_prediction_candidates_Status_AvailableAt;
            ALTER TABLE prediction_candidates RENAME TO prediction_candidates_original;
            CREATE TABLE prediction_candidates (
                RaceId TEXT NOT NULL PRIMARY KEY,
                Status TEXT NOT NULL,
                FirstQueuedAt TEXT NOT NULL,
                AvailableAt TEXT NOT NULL,
                LeaseToken TEXT NULL,
                LeaseExpiresAt TEXT NULL,
                LastError TEXT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IX_prediction_candidates_Status_AvailableAt
                ON prediction_candidates (Status, AvailableAt);
            DROP TABLE prediction_candidates_original;
            """);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreSchemaIncomplete, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.UnknownShape,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.PredictionSchedule).Status);
        Assert.IsEmpty(result.Roots);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task MissingRequiredDatabaseIsNotCreatedAndOptionalQueueRemainsDistinct()
    {
        await using var fixture = await StoreFixture.CreateAsync(createEventStore: false);

        var result = await fixture.CreateEngine().InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreUnavailable, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.Unavailable,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.EventStore).Status);
        Assert.IsFalse(File.Exists(fixture.EventStorePath), "Read-only inspection must not create a missing source file.");
        Assert.AreEqual(MigrationStoreReadStatus.NotConfigured,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.LocalCollectionQueue).Status);
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task UnknownQueueProviderIsUnavailableAndDoesNotCreateAFile()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);

        var result = await fixture.CreateEngine("UnexpectedProvider").InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreUnavailable, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.Unavailable,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.LocalCollectionQueue).Status);
        Assert.IsFalse(File.Exists(fixture.QueuePath));
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task ConfiguredLocalQueueMissingFileIsUnavailableAndNotCreated()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);

        var result = await fixture.CreateEngine("Local").InspectAsync(Manifest(), CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanReason.StoreUnavailable, result.Reason);
        Assert.AreEqual(MigrationStoreReadStatus.Unavailable,
            result.Stores.Single(store => store.Surface == MigrationStoreSurface.LocalCollectionQueue).Status);
        Assert.IsFalse(File.Exists(fixture.QueuePath));
        Assert.IsFalse(result.CanWrite);
    }

    [TestMethod]
    public async Task CancellationStopsInspectionBeforeAnyRootRead()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.CreateEngine().InspectAsync(Manifest(), cancellation.Token));
    }

    [TestMethod]
    public async Task ReadOnlyConnectionRejectsWritesAndLeavesSourceBytesUnchanged()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        await fixture.SeedHorseAsync(OldHorseId);
        var before = await File.ReadAllBytesAsync(fixture.EventStorePath);

        await using var connection = await TypedHorseIdentityStoreReaders.OpenReadOnlyAsync(
            fixture.EventStorePath, CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Horses (HorseId, RegisteredName, NormalizedName, Aliases) VALUES ('horse-22222222-2222-4222-8222-222222222222', 'x', 'x', '[]');";

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        var after = await File.ReadAllBytesAsync(fixture.EventStorePath);
        CollectionAssert.AreEqual(before, after);
    }

    [TestMethod]
    public async Task InvalidManifestDoesNotOpenOrInspectAnyStore()
    {
        await using var fixture = await StoreFixture.CreateAsync(createEventStore: false);
        var manifest = new HorseIdentityMigrationManifest(
        [
            new(OldHorseId, JraIdentity, HypothesisAccepted: false),
        ]);

        var result = await fixture.CreateEngine().InspectAsync(manifest, CancellationToken.None);

        Assert.AreEqual(HorseIdentityStorePlanStatus.InvalidManifest, result.Status);
        Assert.AreEqual(HorseIdentityStorePlanReason.InvalidManifest, result.Reason);
        Assert.IsTrue(result.ManifestErrorCount > 0);
        Assert.AreEqual(0, result.Stores.Count);
        Assert.IsFalse(File.Exists(fixture.EventStorePath));
    }

    private static HorseIdentityMigrationManifest Manifest() => new(
    [
        new(OldHorseId, JraIdentity, HypothesisAccepted: true),
    ]);

    private static string DestinationHorseId()
    {
        var validation = HorseIdentityMigrationManifestPolicy.Validate(Manifest());
        Assert.IsTrue(validation.IsValid);
        return validation.Mappings.Single().DestinationHorseId;
    }

    private sealed class StoreFixture : IAsyncDisposable
    {
        private static readonly string TemporaryParent = Path.Combine(Path.GetTempPath(), "hrp-horse-id-store-engine-tests");
        private readonly string _root;
        private readonly IWebHostEnvironment _environment;

        private StoreFixture(string root)
        {
            _root = root;
            EventStorePath = Path.Combine(root, "eventstore.db");
            CollectionPath = Path.Combine(root, "collection", "collection-platform.db");
            SchedulePath = Path.Combine(root, "schedule", "prediction-executions.db");
            _environment = new FixtureEnvironment(root);
        }

        internal string EventStorePath { get; }
        internal string CollectionPath { get; }
        internal string QueuePath => Path.Combine(_root, "queue", "local-collection-queue.db");
        internal string SchedulePath { get; }

        internal static async Task<StoreFixture> CreateAsync(bool createEventStore = true)
        {
            Directory.CreateDirectory(TemporaryParent);
            var root = Path.Combine(TemporaryParent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var fixture = new StoreFixture(root);
            if (createEventStore)
                await fixture.CreateEventStoreAsync();
            await fixture.CreateCollectionStoreAsync();
            await fixture.CreateScheduleStoreAsync();
            return fixture;
        }

        internal TypedHorseIdentityStoreEngine CreateEngine(string queueProvider = "Sqs")
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:EventStore"] = $"Data Source={EventStorePath};Pooling=False",
            }).Build();
            var collectionOptions = Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = Path.GetDirectoryName(CollectionPath)!,
                DatabaseFileName = Path.GetFileName(CollectionPath),
            });
            var scheduleOptions = Options.Create(new PredictionScheduleOptions
            {
                StateDirectory = Path.GetDirectoryName(SchedulePath)!,
                StoreFileName = Path.GetFileName(SchedulePath),
            });
            var queueOptions = Options.Create(new CollectionQueueOptions
            {
                Provider = queueProvider,
                LocalDatabasePath = QueuePath,
            });
            var readers = new TypedHorseIdentityStoreReaders(configuration, collectionOptions,
                scheduleOptions, queueOptions, _environment);
            return new(readers);
        }

        internal async Task SeedHorseAsync(string horseId)
        {
            await using var connection = new SqliteConnection($"Data Source={EventStorePath};Pooling=False");
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var horse = connection.CreateCommand())
            {
                horse.Transaction = (SqliteTransaction)transaction;
                horse.CommandText = """
                    INSERT INTO Horses
                        (HorseId, RegisteredName, NormalizedName, SexCode, BirthDate, OwnerName, BreederName,
                         SireName, DamName, DamsireName, CoatColor, Aliases)
                    VALUES ($id, 'Fixture Horse', 'FixtureHorse', NULL, NULL, NULL, NULL,
                            NULL, NULL, NULL, NULL, '[]');
                    """;
                horse.Parameters.AddWithValue("$id", horseId);
                await horse.ExecuteNonQueryAsync();
            }
            await using (var eventRow = connection.CreateCommand())
            {
                eventRow.Transaction = (SqliteTransaction)transaction;
                eventRow.CommandText = """
                    INSERT INTO EventEntity
                        (AggregateId, AggregateName, AggregateSequenceNumber, BatchId, Data, Metadata)
                    VALUES ($id, 'HorseAggregate', 1, $batch, '{}', '{}');
                    """;
                eventRow.Parameters.AddWithValue("$id", horseId);
                eventRow.Parameters.AddWithValue("$batch", Guid.NewGuid().ToString());
                await eventRow.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }

        internal async Task SeedJraResourceAsync(string resourceId)
        {
            await using var connection = new SqliteConnection($"Data Source={CollectionPath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO collection_resources (Type, Provider, ResourceId, AttributesJson, CreatedAt)
                VALUES ('Horse', 'JRA', $resource, '{}', '2026-10-14 00:00:00');
                """;
            command.Parameters.AddWithValue("$resource", resourceId);
            await command.ExecuteNonQueryAsync();
        }

        internal async Task ExecuteAsync(string path, string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        internal async Task<string> SourceSnapshotAsync()
        {
            var eventHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(EventStorePath)));
            var collectionHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(CollectionPath)));
            var scheduleHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(SchedulePath)));
            return $"{eventHash}:{collectionHash}:{scheduleHash}";
        }

        private async Task CreateEventStoreAsync()
        {
            var options = new DbContextOptionsBuilder<EventStoreDbContext>()
                .UseSqlite($"Data Source={EventStorePath};Pooling=False").Options;
            await using var context = new EventStoreDbContext(options);
            await context.Database.MigrateAsync();
        }

        private async Task CreateCollectionStoreAsync()
        {
            _ = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
            {
                StateDirectory = Path.GetDirectoryName(CollectionPath)!,
                DatabaseFileName = Path.GetFileName(CollectionPath),
            }));
        }

        private async Task CreateScheduleStoreAsync()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SchedulePath)!);
            _ = new PredictionScheduleStore(Options.Create(new PredictionScheduleOptions
            {
                StateDirectory = Path.GetDirectoryName(SchedulePath)!,
                StoreFileName = Path.GetFileName(SchedulePath),
            }));
        }

        public ValueTask DisposeAsync()
        {
            if (!Path.GetFullPath(_root).StartsWith(Path.GetFullPath(TemporaryParent) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to delete a path outside the owned test root.");
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }

        private sealed class FixtureEnvironment(string root) : IWebHostEnvironment
        {
            public string ApplicationName { get; set; } = "HorseIdentityMigrationStoreEngineTests";
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public string WebRootPath { get; set; } = root;
            public string EnvironmentName { get; set; } = "Testing";
            public string ContentRootPath { get; set; } = root;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
