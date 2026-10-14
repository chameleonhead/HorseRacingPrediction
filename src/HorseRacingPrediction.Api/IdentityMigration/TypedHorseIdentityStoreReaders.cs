using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EventFlow.Aggregates;
using EventFlow.EventStores;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.PredictionScheduling;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal enum MigrationStoreSurface
{
    EventStore,
    CollectionPlatform,
    PredictionSchedule,
    LocalCollectionQueue,
}

internal enum MigrationStoreReadStatus
{
    Observed,
    NotConfigured,
    Unavailable,
    UnknownShape,
    Unsupported,
    Truncated,
}

internal enum MigrationRootStatus
{
    Present,
    Missing,
    Duplicate,
    TargetCollision,
    UnknownShape,
    Unavailable,
}

internal sealed record MigrationStoreSchema(
    MigrationStoreSurface Surface,
    MigrationStoreReadStatus Status,
    int TableCount,
    int UnknownObjectCount,
    string? Fingerprint);

internal sealed record MigrationRootProbe(
    string OldHorseId,
    string DestinationHorseId,
    MigrationRootStatus SourceStatus,
    bool TargetHorseRootExists,
    bool TargetEventStreamExists,
    bool TargetSnapshotExists,
    bool TargetCollectionResourceExists,
    bool TargetSuppressionExists,
    bool TargetSelectionExists,
    bool TargetFailureNotificationExists,
    bool CollectionResourceNaturalKeyCollision,
    bool SuppressionNaturalKeyCollision)
{
    internal bool HasTargetCollision => TargetHorseRootExists || TargetEventStreamExists || TargetSnapshotExists
        || CollectionResourceNaturalKeyCollision || SuppressionNaturalKeyCollision;
}

internal sealed record MigrationStoreReadSnapshot(
    IReadOnlyList<MigrationStoreSchema> Schemas,
    IReadOnlyList<MigrationRootProbe> Roots,
    bool TypedReferenceClosureInspected,
    TypedHorseIdentityReferenceReadResult? TypedReferences);

/// <summary>
/// Reads configured SQLite surfaces without resolving normal store constructors or triggering schema setup.
/// This first slice deliberately never claims full reference closure and never exposes a writer.
/// </summary>
internal sealed class TypedHorseIdentityStoreReaders(
    IConfiguration configuration,
    IOptions<CollectionPlatformOptions> collectionOptions,
    IOptions<PredictionScheduleOptions> scheduleOptions,
    IOptions<CollectionQueueOptions> queueOptions,
    IWebHostEnvironment environment,
    IEventJsonSerializer? eventJsonSerializer = null,
    IDomainEventFactory? domainEventFactory = null)
{
    private const int CommandTimeoutSeconds = 1;
    private const int SchemaObjectPageSize = 128;
    private const int MaximumSchemaObjects = 512;
    private const int MaximumSchemaSqlBytes = 8192;
    private const int MaximumMigrationHistoryRows = 512;
    private const int CurrentCollectionPlatformSchemaVersion = 25;
    private const int MaximumCollectionSchemaHistoryRows = CurrentCollectionPlatformSchemaVersion;

    private static readonly string[] EventStoreTables =
    [
        "EventEntity", "SnapshotEntity", "HorseIdentityRepairCandidates", "HorseIdentityRepairRedirects",
        "HorseRaceHistories", "Horses", "HorseWeightHistories", "JockeyRaceHistories", "Jockeys",
        "JraSubjectProfileReadModel", "MemoSubjects", "OwnerAliasMappings", "OwnerMergeAudits",
        "PredictionComparisons", "PredictionTickets", "RacePredictionContexts", "RaceResults",
        "RaceSummaries", "SubjectIdentificationRepairIssues", "Trainers",
    ];

    private static readonly string[] CollectionPlatformTables =
    [
        "race_repair_holds", "collection_resources", "collection_resource_suppressions", "collection_definitions",
        "collection_revisions", "collection_revision_impacts", "collection_states", "collection_requests",
        "collection_request_batch_bindings", "collection_tasks", "collection_active_tasks", "collection_attempts",
        "resource_locations", "collection_task_outbox", "collection_attempt_stage_outcomes", "race_artifact_states",
        "race_scheduling_evidence", "collection_execution_leases", "collection_dispatcher_fairness_state",
        "collection_platform_controls", "collection_failure_notifications",
        "collection_subject_identification_selections", "collection_backfill_batches",
        "collection_batch_recovery_progress", "collection_batch_recovery_scan",
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, ExpectedTable>> EventStoreModel =
        new(BuildExpectedEventStoreModel);
    private static readonly Lazy<IReadOnlyDictionary<string, ExpectedTable>> CollectionPlatformModel =
        new(BuildExpectedCollectionPlatformModel);
    private static readonly Lazy<IReadOnlyDictionary<string, ExpectedTable>> CollectionPlatformCurrentUpgradeModel =
        new(BuildExpectedCollectionPlatformCurrentUpgradeModel);
    private static readonly Lazy<IReadOnlyDictionary<string, ExpectedTable>> CollectionPlatformV6UpgradeModel =
        new(BuildExpectedCollectionPlatformV6UpgradeModel);
    private static readonly Lazy<IReadOnlyList<string>> EventStoreMigrationIds = new(
        static () =>
        {
            using var context = CreateEventStoreModel();
            return context.Database.GetMigrations().Order(StringComparer.Ordinal).ToArray();
        });

    internal async Task<MigrationStoreReadSnapshot> InspectAsync(
        IReadOnlyList<HorseIdentityMigrationMapping> mappings,
        CancellationToken cancellationToken)
    {
        var configured = ResolveConfiguredStores();
        var schemas = new List<MigrationStoreSchema>(configured.Count);
        foreach (var store in configured)
            schemas.Add(await InspectSchemaAsync(store, cancellationToken).ConfigureAwait(false));

        if (schemas.Any(schema => schema.Status is not (MigrationStoreReadStatus.Observed
                or MigrationStoreReadStatus.NotConfigured)))
            return new(schemas, [], TypedReferenceClosureInspected: false, TypedReferences: null);

        var eventStore = configured.Single(store => store.Surface == MigrationStoreSurface.EventStore);
        var collection = configured.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform);
        var roots = await InspectRootsAsync(eventStore.Path!, collection.Path!, mappings, cancellationToken)
            .ConfigureAwait(false);

        var typedReferences = await new TypedHorseIdentityReferenceReader(eventJsonSerializer, domainEventFactory).InspectAsync(
            eventStore.Path!, collection.Path!,
            configured.Single(store => store.Surface == MigrationStoreSurface.PredictionSchedule).Path!,
            configured.Single(store => store.Surface == MigrationStoreSurface.LocalCollectionQueue).Path,
            mappings, cancellationToken).ConfigureAwait(false);
        return new(schemas, roots, typedReferences.IsComplete, typedReferences);
    }

    internal TypedHorseIdentityReferenceReader CreateReferenceReaderForExecution() =>
        new(eventJsonSerializer, domainEventFactory);

    internal async Task<SqliteConnection?> OpenConfiguredStoreForExecutionAsync(
        MigrationStoreSurface surface, CancellationToken cancellationToken)
    {
        var store = ResolveConfiguredStores().SingleOrDefault(candidate => candidate.Surface == surface);
        if (store is null || string.IsNullOrWhiteSpace(store.Path) || !File.Exists(store.Path))
        {
            if (store is { Required: false }) return null;
            throw new InvalidOperationException("Configured migration store is unavailable.");
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = store.Path,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private List<ConfiguredStore> ResolveConfiguredStores()
    {
        var eventConnectionString = configuration.GetConnectionString("EventStore") ?? "Data Source=eventstore.db";
        var eventDataSource = new SqliteConnectionStringBuilder(eventConnectionString).DataSource;
        var eventStorePath = string.IsNullOrWhiteSpace(eventDataSource) || eventDataSource == ":memory:"
            ? null
            : ResolvePath(eventDataSource, environment.ContentRootPath);
        var stores = new List<ConfiguredStore>
        {
            new(MigrationStoreSurface.EventStore, eventStorePath, Required: true,
                EventStoreTables, ["__EFMigrationsHistory"]),
        };

        var collectionPath = CollectionPlatformStore.ResolveIdentityMigrationPreflightDatabasePath(collectionOptions);
        stores.Add(new(MigrationStoreSurface.CollectionPlatform,
            ResolvePath(collectionPath, environment.ContentRootPath), Required: true,
            CollectionPlatformTables, ["collection_schema_history"]));

        var schedule = scheduleOptions.Value;
        var scheduleDirectory = string.IsNullOrWhiteSpace(schedule.StateDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "prediction-scheduling-state")
            : schedule.StateDirectory;
        var schedulePath = Path.Combine(scheduleDirectory,
            string.IsNullOrWhiteSpace(schedule.StoreFileName) ? "prediction-executions.db" : schedule.StoreFileName);
        stores.Add(new(MigrationStoreSurface.PredictionSchedule,
            ResolvePath(schedulePath, Environment.CurrentDirectory), Required: true,
            ["prediction_candidates"], []));

        var queue = queueOptions.Value;
        if (string.Equals(queue.Provider, "Sqs", StringComparison.OrdinalIgnoreCase))
        {
            stores.Add(new(MigrationStoreSurface.LocalCollectionQueue, null, Required: false, ["local_collection_messages"], []));
        }
        else if (string.Equals(queue.Provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            var queuePath = string.IsNullOrWhiteSpace(queue.LocalDatabasePath)
                ? "collection-platform-state/local-collection-queue.db"
                : queue.LocalDatabasePath;
            stores.Add(new(MigrationStoreSurface.LocalCollectionQueue,
                ResolvePath(queuePath, Environment.CurrentDirectory), Required: true,
                ["local_collection_messages"], []));
        }
        else
        {
            stores.Add(new(MigrationStoreSurface.LocalCollectionQueue, null, Required: true,
                ["local_collection_messages"], []));
        }

        return stores;
    }

    private static string? ResolvePath(string path, string baseDirectory)
    {
        try
        {
            return Path.GetFullPath(path, baseDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static EventStoreDbContext CreateEventStoreModel()
    {
        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        return new EventStoreDbContext(options);
    }

    private static CollectionPlatformDbContext CreateCollectionPlatformModel()
    {
        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        return new CollectionPlatformDbContext(options);
    }

    private static IReadOnlyDictionary<string, ExpectedTable> BuildExpectedCollectionPlatformModel()
    {
        var model = new Dictionary<string, ExpectedTable>(
            BuildExpectedModel(CreateCollectionPlatformModel()), StringComparer.OrdinalIgnoreCase);

        // This table is created by CollectionPlatformSchemaMigrator v18 rather than by the
        // base EF model when an existing platform database is upgraded. Keep the exact source
        // DDL as the canonical shape for both fresh and upgraded fixtures.
        model["race_repair_holds"] = new(
            new Dictionary<string, ExpectedColumn>(StringComparer.OrdinalIgnoreCase)
            {
                ["RaceId"] = new("TEXT", false, 1),
                ["Generation"] = new("INTEGER", false, 2),
                ["OperationId"] = new("TEXT", false, 0),
                ["Reason"] = new("TEXT", false, 0),
                ["CreatedAt"] = new("TEXT", false, 0),
                ["ReleasedAt"] = new("TEXT", true, 0),
                ["AssignmentFingerprint"] = new("TEXT", true, 0),
                ["ReleaseOperationId"] = new("TEXT", true, 0),
            },
            new Dictionary<string, ExpectedIndex>(StringComparer.OrdinalIgnoreCase)
            {
                ["IX_race_repair_holds_OperationId"] = new(["OperationId"], true, null),
                ["IX_race_repair_holds_RaceId"] = new(["RaceId"], true, "ReleasedAt IS NULL"),
            });
        return model;
    }

    private static IReadOnlyDictionary<string, ExpectedTable> BuildExpectedCollectionPlatformCurrentUpgradeModel()
    {
        var model = new Dictionary<string, ExpectedTable>(CollectionPlatformModel.Value,
            StringComparer.OrdinalIgnoreCase);

        static void SetDefault(Dictionary<string, ExpectedTable> tables, string table, string column,
            string expression)
        {
            var shape = tables[table];
            var columns = new Dictionary<string, ExpectedColumn>(shape.Columns, StringComparer.OrdinalIgnoreCase)
            {
                [column] = shape.Columns[column] with { DefaultExpression = expression },
            };
            tables[table] = shape with { Columns = columns };
        }

        SetDefault(model, "collection_dispatcher_fairness_state", "ConsecutiveRealtime", "0");
        SetDefault(model, "collection_dispatcher_fairness_state", "ReservationSequence", "0");
        SetDefault(model, "collection_subject_identification_selections", "CreatedTask", "1");
        SetDefault(model, "collection_subject_identification_selections", "ReusedTask", "0");

        var failure = model["collection_failure_notifications"];
        model["collection_failure_notifications"] = failure with
        {
            Columns = new Dictionary<string, ExpectedColumn>(failure.Columns, StringComparer.OrdinalIgnoreCase)
            {
                ["CanonicalResourceType"] = failure.Columns["CanonicalResourceType"] with { Type = "TEXT" },
                ["ResolutionStatus"] = failure.Columns["ResolutionStatus"] with { DefaultExpression = null },
            },
        };
        return model;
    }

    private static IReadOnlyDictionary<string, ExpectedTable> BuildExpectedCollectionPlatformV6UpgradeModel()
    {
        var model = new Dictionary<string, ExpectedTable>(CollectionPlatformCurrentUpgradeModel.Value,
            StringComparer.OrdinalIgnoreCase);

        static void SetDefault(Dictionary<string, ExpectedTable> tables, string table, string column,
            string expression)
        {
            var shape = tables[table];
            var columns = new Dictionary<string, ExpectedColumn>(shape.Columns, StringComparer.OrdinalIgnoreCase)
            {
                [column] = shape.Columns[column] with { DefaultExpression = expression },
            };
            tables[table] = shape with { Columns = columns };
        }

        SetDefault(model, "collection_requests", "Lane", "'Normal'");
        SetDefault(model, "collection_requests", "Priority", "50");
        SetDefault(model, "collection_requests", "MetadataJson", "'{}'");
        SetDefault(model, "collection_tasks", "RaceHoldGeneration", "0");
        return model;
    }

    private static IReadOnlyDictionary<string, ExpectedTable> BuildExpectedEventStoreModel()
    {
        var model = new Dictionary<string, ExpectedTable>(
            BuildExpectedModel(CreateEventStoreModel()), StringComparer.OrdinalIgnoreCase);
        var raceContextColumns = new Dictionary<string, ExpectedColumn>(
            model["RacePredictionContexts"].Columns, StringComparer.OrdinalIgnoreCase)
        {
            ["OddsSnapshots"] = new("TEXT", false, 0, "'[]'"),
        };
        model["RacePredictionContexts"] = model["RacePredictionContexts"] with
        {
            Columns = raceContextColumns,
        };
        var raceContexts = model["RacePredictionContexts"];
        var indexes = new Dictionary<string, ExpectedIndex>(raceContexts.Indexes,
            StringComparer.OrdinalIgnoreCase)
        {
            ["IX_RacePredictionContexts_RaceDate_RaceNumber"] =
                new(["RaceDate", "RaceNumber"], false, null),
        };
        model["RacePredictionContexts"] = raceContexts with { Indexes = indexes };
        return model;
    }

    private static IReadOnlyDictionary<string, ExpectedTable> BuildExpectedModel(DbContext context)
    {
        using (context)
        {
            var tables = new Dictionary<string, ExpectedTable>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in context.Model.GetRelationalModel().Tables)
            {
                var primaryKeyColumns = table.PrimaryKey?.Columns ?? [];
                var columns = table.Columns.ToDictionary(column => column.Name,
                    column => new ExpectedColumn(column.StoreType, column.IsNullable,
                        Array.IndexOf(primaryKeyColumns.ToArray(), column) + 1,
                        NormalizeExpectedDefault(column.DefaultValue, column.DefaultValueSql)),
                    StringComparer.OrdinalIgnoreCase);
                var indexes = table.Indexes.ToDictionary(index => index.Name,
                    index => new ExpectedIndex(index.Columns.Select(column => column.Name).ToArray(),
                        index.IsUnique, index.Filter),
                    StringComparer.OrdinalIgnoreCase);
                var foreignKeys = table.ForeignKeyConstraints
                    .Select(constraint => new ExpectedForeignKey(
                        constraint.Columns.Select(column => column.Name).ToArray(),
                        constraint.PrincipalTable.Name,
                        constraint.PrincipalColumns.Select(column => column.Name).ToArray(),
                        NormalizeReferentialAction(constraint.OnDeleteAction.ToString()),
                        "NO ACTION"))
                    .OrderBy(constraint => constraint.PrincipalTable, StringComparer.Ordinal)
                    .ThenBy(constraint => string.Join("\u001f", constraint.Columns), StringComparer.Ordinal)
                    .ToArray();
                tables.Add(table.Name, new(columns, indexes, foreignKeys));
            }
            return tables;
        }
    }

    private static IReadOnlyDictionary<string, ExpectedTable> GetExpectedTables(MigrationStoreSurface surface)
    {
        if (surface == MigrationStoreSurface.EventStore)
            return EventStoreModel.Value;
        if (surface == MigrationStoreSurface.CollectionPlatform)
            return CollectionPlatformModel.Value;
        if (surface == MigrationStoreSurface.PredictionSchedule)
            return new Dictionary<string, ExpectedTable>(StringComparer.OrdinalIgnoreCase)
            {
                ["prediction_candidates"] = new(
                    new Dictionary<string, ExpectedColumn>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["RaceId"] = new("TEXT", false, 1),
                        ["Status"] = new("INTEGER", false, 0),
                        ["FirstQueuedAt"] = new("TEXT", false, 0),
                        ["AvailableAt"] = new("TEXT", false, 0),
                        ["LeaseToken"] = new("TEXT", true, 0),
                        ["LeaseExpiresAt"] = new("TEXT", true, 0),
                        ["LastError"] = new("TEXT", true, 0),
                        ["UpdatedAt"] = new("TEXT", false, 0),
                    },
                    new Dictionary<string, ExpectedIndex>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["IX_prediction_candidates_Status_AvailableAt"] = new(["Status", "AvailableAt"], false, null),
                    }),
            };
        if (surface == MigrationStoreSurface.LocalCollectionQueue)
            return new Dictionary<string, ExpectedTable>(StringComparer.OrdinalIgnoreCase)
            {
                ["local_collection_messages"] = new(
                    new Dictionary<string, ExpectedColumn>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["message_id"] = new("INTEGER", false, 1),
                        ["body"] = new("TEXT", false, 0),
                        ["visible_at"] = new("TEXT", false, 0),
                        ["receipt_handle"] = new("TEXT", true, 0),
                        ["receive_count"] = new("INTEGER", false, 0, "0"),
                        ["dead_letter"] = new("INTEGER", false, 0, "0"),
                        ["created_at"] = new("TEXT", false, 0),
                    }, new Dictionary<string, ExpectedIndex>(StringComparer.OrdinalIgnoreCase)),
            };
        throw new ArgumentOutOfRangeException(nameof(surface));
    }

    private static async Task<MigrationStoreSchema> InspectSchemaAsync(
        ConfiguredStore store,
        CancellationToken cancellationToken)
    {
        if (!store.Required)
            return new(store.Surface, MigrationStoreReadStatus.NotConfigured, 0, 0, null);
        if (store.Path is null || !File.Exists(store.Path))
            return new(store.Surface, MigrationStoreReadStatus.Unavailable, 0, 0, null);

        try
        {
            await using var connection = await OpenReadOnlyAsync(store.Path, cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
            var schemaScan = await ReadSchemaObjectsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var objects = schemaScan.Objects;
            if (schemaScan.Truncated)
                return new(store.Surface, MigrationStoreReadStatus.Truncated, 0, 0, null);

            var acceptedMetadataTables = MetadataTableShapes(store.Surface);
            var tableObjects = objects.Where(item => item.Type == "table")
                .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
            var tables = tableObjects.Keys
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var collectionHistoryLayout = CollectionSchemaHistoryLayout.Unknown;
            var expectedShapes = GetExpectedTables(store.Surface);
            IReadOnlyList<IReadOnlyDictionary<string, ExpectedTable>> shapeCandidates = [expectedShapes];
            var malformedSchema = false;
            if (store.Surface == MigrationStoreSurface.CollectionPlatform
                && tables.Contains("collection_schema_history", StringComparer.OrdinalIgnoreCase))
            {
                collectionHistoryLayout = await ReadCollectionSchemaHistoryLayoutAsync(
                    connection, transaction, cancellationToken).ConfigureAwait(false);
                if (collectionHistoryLayout == CollectionSchemaHistoryLayout.Unknown)
                    malformedSchema = true;
                if (collectionHistoryLayout == CollectionSchemaHistoryLayout.Upgrade)
                {
                    expectedShapes = CollectionPlatformCurrentUpgradeModel.Value;
                    shapeCandidates = [
                        CollectionPlatformCurrentUpgradeModel.Value,
                        CollectionPlatformModel.Value,
                    ];
                }
                else if (collectionHistoryLayout == CollectionSchemaHistoryLayout.UpgradeV6)
                {
                    expectedShapes = CollectionPlatformV6UpgradeModel.Value;
                    shapeCandidates = [
                        CollectionPlatformV6UpgradeModel.Value,
                        CollectionPlatformCurrentUpgradeModel.Value,
                        CollectionPlatformModel.Value,
                    ];
                }
                else if (collectionHistoryLayout == CollectionSchemaHistoryLayout.UpgradeV18)
                {
                    expectedShapes = CollectionPlatformCurrentUpgradeModel.Value;
                    shapeCandidates = [
                        CollectionPlatformCurrentUpgradeModel.Value,
                        CollectionPlatformModel.Value,
                    ];
                }
            }
            var expectedTables = expectedShapes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingTables = !expectedTables.SetEquals(store.ExpectedTables)
                || expectedTables.Any(table => !tables.Contains(table));
            var expectedIndexes = expectedShapes.Values.SelectMany(table => table.Indexes.Keys)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknownObjects = objects.Count(item => item.Type switch
            {
                "table" => !expectedTables.Contains(item.Name) && !acceptedMetadataTables.ContainsKey(item.Name),
                "index" => !expectedIndexes.Contains(item.Name),
                _ => true,         // Views/triggers are not part of the source schema contract.
            });
            malformedSchema |= unknownObjects > 0
                || store.MetadataTables.Any(table => !tables.Contains(table));
            var matchedShape = false;
            foreach (var candidate in shapeCandidates)
            {
                var candidateMatches = true;
                var requireCanonicalFairnessCheck = ReferenceEquals(candidate,
                    CollectionPlatformCurrentUpgradeModel.Value)
                    || ReferenceEquals(candidate, CollectionPlatformV6UpgradeModel.Value);
                foreach (var tableName in tables)
                {
                    var expected = candidate.TryGetValue(tableName, out var modelShape)
                        ? modelShape
                        : acceptedMetadataTables.GetValueOrDefault(tableName);
                    if (expected is null || !await MatchesTableShapeAsync(connection, transaction,
                            tableName, expected, tableObjects[tableName].Sql,
                            requireCanonicalFairnessCheck,
                            cancellationToken).ConfigureAwait(false))
                    {
                        candidateMatches = false;
                        break;
                    }
                }
                if (candidateMatches)
                {
                    matchedShape = true;
                    break;
                }
            }
            malformedSchema |= !matchedShape;
            if (store.Surface == MigrationStoreSurface.EventStore
                && tables.Contains("__EFMigrationsHistory", StringComparer.OrdinalIgnoreCase)
                && !await MatchesEventStoreMigrationHistoryAsync(connection, transaction, cancellationToken)
                    .ConfigureAwait(false))
            {
                malformedSchema = true;
            }
            var fingerprint = Fingerprint(objects);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            var status = objects.Any(item => item.SqlBytes > MaximumSchemaSqlBytes)
                ? MigrationStoreReadStatus.Truncated
                : malformedSchema
                    ? MigrationStoreReadStatus.UnknownShape
                    : missingTables
                        ? MigrationStoreReadStatus.Unsupported
                        : MigrationStoreReadStatus.Observed;
            return new(store.Surface, status, tables.Count, unknownObjects, fingerprint);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException)
        {
            return new(store.Surface, MigrationStoreReadStatus.Unavailable, 0, 0, null);
        }
        catch (IOException)
        {
            return new(store.Surface, MigrationStoreReadStatus.Unavailable, 0, 0, null);
        }
    }

    private static async Task<SchemaScan> ReadSchemaObjectsAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var objects = new List<SchemaObject>(SchemaObjectPageSize);
        string? afterType = null;
        string? afterName = null;
        while (true)
        {
            var page = await ReadSchemaObjectPageAsync(connection, transaction, afterType, afterName,
                cancellationToken).ConfigureAwait(false);
            var hasMore = page.Count > SchemaObjectPageSize;
            var take = Math.Min(page.Count, SchemaObjectPageSize);
            if (objects.Count + take > MaximumSchemaObjects)
                return new(objects, Truncated: true);
            for (var index = 0; index < take; index++)
                objects.Add(page[index]);
            if (!hasMore)
                return new(objects, Truncated: false);

            var last = page[take - 1];
            afterType = last.Type;
            afterName = last.Name;
            if (objects.Count >= MaximumSchemaObjects)
                return new(objects, Truncated: true);
        }
    }

    private static async Task<List<SchemaObject>> ReadSchemaObjectPageAsync(
        SqliteConnection connection, SqliteTransaction transaction, string? afterType, string? afterName,
        CancellationToken cancellationToken)
    {
        var objects = new List<SchemaObject>(SchemaObjectPageSize + 1);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT type,
                   CASE WHEN length(CAST(name AS BLOB)) <= 512 THEN name ELSE NULL END,
                   length(CAST(name AS BLOB)),
                   CASE WHEN length(CAST(sql AS BLOB)) <= $maxBytes THEN sql ELSE NULL END,
                   COALESCE(length(CAST(sql AS BLOB)), 0)
            FROM sqlite_master
            WHERE type IN ('table', 'index', 'view', 'trigger') AND name NOT GLOB 'sqlite_*'
              AND ($afterType IS NULL OR type > $afterType OR (type = $afterType AND name > $afterName))
            ORDER BY type, name LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$maxBytes", MaximumSchemaSqlBytes);
        command.Parameters.AddWithValue("$afterType", (object?)afterType ?? DBNull.Value);
        command.Parameters.AddWithValue("$afterName", (object?)afterName ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", SchemaObjectPageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(1) || reader.GetInt64(2) > 512)
                return [new("truncated", "oversized-object-name", null, long.MaxValue)];
            objects.Add(new(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt64(4)));
        }
        return objects;
    }

    private static Dictionary<string, ExpectedTable> MetadataTableShapes(MigrationStoreSurface surface)
    {
        if (surface == MigrationStoreSurface.EventStore)
            return new(StringComparer.OrdinalIgnoreCase)
            {
                ["__EFMigrationsHistory"] = new(
                    new Dictionary<string, ExpectedColumn>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["MigrationId"] = new("TEXT", false, 1),
                        ["ProductVersion"] = new("TEXT", false, 0),
                    }, new Dictionary<string, ExpectedIndex>(StringComparer.OrdinalIgnoreCase)),
                ["__EFMigrationsLock"] = new(
                    new Dictionary<string, ExpectedColumn>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Id"] = new("INTEGER", false, 1),
                        ["Timestamp"] = new("TEXT", false, 0),
                    }, new Dictionary<string, ExpectedIndex>(StringComparer.OrdinalIgnoreCase)),
            };
        if (surface == MigrationStoreSurface.CollectionPlatform)
            return new(StringComparer.OrdinalIgnoreCase)
            {
                ["collection_schema_history"] = new(
                    new Dictionary<string, ExpectedColumn>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["version"] = new("INTEGER", false, 1),
                        ["applied_at"] = new("TEXT", false, 0),
                    }, new Dictionary<string, ExpectedIndex>(StringComparer.OrdinalIgnoreCase)),
            };
        if (surface == MigrationStoreSurface.LocalCollectionQueue)
            return new(StringComparer.OrdinalIgnoreCase)
            {
                // SQLite creates this internal table for the queue's AUTOINCREMENT primary key.
                ["sqlite_sequence"] = new(
                    new Dictionary<string, ExpectedColumn>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = new("TEXT", true, 0),
                        ["seq"] = new("INTEGER", true, 0),
                    }, new Dictionary<string, ExpectedIndex>(StringComparer.OrdinalIgnoreCase)),
            };
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<bool> MatchesTableShapeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        ExpectedTable expected,
        string? tableSql,
        bool allowCanonicalFairnessCheck,
        CancellationToken cancellationToken)
    {
        var sqliteInternalSequence = tableName.Equals("sqlite_sequence", StringComparison.OrdinalIgnoreCase)
            && tableSql is null;
        if (!sqliteInternalSequence && !MatchesSupportedTableSyntax(tableName, tableSql,
                allowCanonicalFairnessCheck))
            return false;

        var actualColumns = new Dictionary<string, ActualColumn>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = $"PRAGMA table_xinfo({QuoteIdentifier(tableName)});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (reader.FieldCount < 7)
                return false;
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (actualColumns.Count >= 512)
                    return false;
                if (reader.GetInt32(6) != 0)
                    return false;
                actualColumns[reader.GetString(1)] = new(
                    reader.GetString(2), reader.GetInt32(3) != 0, reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt32(5));
            }
        }

        if (actualColumns.Count != expected.Columns.Count)
        {
            return false;
        }
        foreach (var (name, expectedColumn) in expected.Columns)
        {
            if (!actualColumns.TryGetValue(name, out var actual)
                || !string.Equals(actual.Type, expectedColumn.Type, StringComparison.OrdinalIgnoreCase)
                || actual.PrimaryKeyOrdinal != expectedColumn.PrimaryKeyOrdinal
                || IsActualColumnNullable(actualColumns, actual) != expectedColumn.Nullable
                || !string.Equals(NormalizeSqlExpression(actual.DefaultValue),
                    NormalizeSqlExpression(expectedColumn.DefaultExpression), StringComparison.Ordinal))
            {
                return false;
            }
        }

        var actualForeignKeys = await ReadForeignKeysAsync(connection, transaction, tableName, cancellationToken)
            .ConfigureAwait(false);
        var expectedForeignKeys = (expected.ForeignKeys ?? [])
            .OrderBy(foreignKey => foreignKey.PrincipalTable, StringComparer.Ordinal)
            .ThenBy(foreignKey => string.Join("\u001f", foreignKey.Columns), StringComparer.Ordinal)
            .ToArray();
        if (actualForeignKeys is null
            || actualForeignKeys.Count != expectedForeignKeys.Length
            || !actualForeignKeys.OrderBy(foreignKey => foreignKey.PrincipalTable, StringComparer.Ordinal)
                .ThenBy(foreignKey => string.Join("\u001f", foreignKey.Columns), StringComparer.Ordinal)
                .Zip(expectedForeignKeys).All(pair =>
                pair.First.Columns.SequenceEqual(pair.Second.Columns, StringComparer.OrdinalIgnoreCase)
                && string.Equals(pair.First.PrincipalTable, pair.Second.PrincipalTable,
                    StringComparison.OrdinalIgnoreCase)
                && pair.First.PrincipalColumns.SequenceEqual(pair.Second.PrincipalColumns,
                    StringComparer.OrdinalIgnoreCase)
                && string.Equals(pair.First.OnDelete, pair.Second.OnDelete, StringComparison.Ordinal)
                && string.Equals(pair.First.OnUpdate, pair.Second.OnUpdate, StringComparison.Ordinal)
                && string.Equals(pair.First.Match, "NONE", StringComparison.Ordinal)))
        {
            return false;
        }

        var actualIndexes = new Dictionary<string, ActualIndex>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = $"PRAGMA index_list({QuoteIdentifier(tableName)});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3) || reader.IsDBNull(4))
                    return false;
                var name = reader.GetString(1);
                if (actualIndexes.Count >= 128)
                    return false;
                var origin = reader.GetString(3).ToLowerInvariant();
                if (origin is not ("c" or "pk" or "u"))
                    return false;
                actualIndexes[name] = new(reader.GetInt32(2) != 0, reader.GetInt32(4) != 0, origin,
                    await ReadIndexPredicateAsync(connection, transaction, name, cancellationToken)
                        .ConfigureAwait(false));
            }
        }

        var expectedPrimaryKey = expected.Columns
            .Where(pair => pair.Value.PrimaryKeyOrdinal > 0)
            .OrderBy(pair => pair.Value.PrimaryKeyOrdinal)
            .Select(pair => pair.Key)
            .ToArray();
        var primaryKeyIndexes = actualIndexes.Where(pair => pair.Value.Origin == "pk").ToArray();
        var uniqueConstraintIndexes = actualIndexes.Where(pair => pair.Value.Origin == "u").ToArray();
        if (uniqueConstraintIndexes.Length > 0
            || (expectedPrimaryKey.Length > 0
                && !IsImplicitIntegerRowId(expected.Columns, expectedPrimaryKey)
                && primaryKeyIndexes.Length != 1)
            || (IsImplicitIntegerRowId(expected.Columns, expectedPrimaryKey)
                && primaryKeyIndexes.Length != 0))
            return false;
        if (primaryKeyIndexes.Length == 1)
        {
            var primaryKeyColumns = await ReadIndexColumnsAsync(connection, transaction,
                primaryKeyIndexes[0].Key, cancellationToken).ConfigureAwait(false);
            if (primaryKeyColumns.Count != expectedPrimaryKey.Length
                || !primaryKeyColumns.Select(column => column.Name)
                    .SequenceEqual(expectedPrimaryKey, StringComparer.OrdinalIgnoreCase)
                || primaryKeyColumns.Any(column => column.Descending
                    || !string.Equals(column.Collation, "BINARY", StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        var userIndexes = actualIndexes.Where(pair => pair.Value.Origin == "c")
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (userIndexes.Count != expected.Indexes.Count
            || !userIndexes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(expected.Indexes.Keys))
        {
            return false;
        }

        foreach (var (indexName, expectedIndex) in expected.Indexes)
        {
            if (!userIndexes.TryGetValue(indexName, out var actualIndex))
                return false;
            if (actualIndex.Unique != expectedIndex.Unique
                || actualIndex.Partial != (expectedIndex.Predicate is not null)
                || !string.Equals(actualIndex.Predicate, NormalizeSqlExpression(expectedIndex.Predicate),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var actualIndexColumns = await ReadIndexColumnsAsync(connection, transaction, indexName, cancellationToken)
                .ConfigureAwait(false);
            if (actualIndexColumns.Count != expectedIndex.Columns.Count
                || !actualIndexColumns.Select(column => column.Name)
                    .SequenceEqual(expectedIndex.Columns, StringComparer.OrdinalIgnoreCase)
                || actualIndexColumns.Any(column => column.Descending
                    || !string.Equals(column.Collation, "BINARY", StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        return true;
    }

    private static async Task<IReadOnlyList<ActualForeignKey>?> ReadForeignKeysAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        var rows = new Dictionary<int, List<(int Sequence, string Column, string PrincipalColumn, string PrincipalTable,
            string OnDelete, string OnUpdate, string Match)>>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = $"PRAGMA foreign_key_list({QuoteIdentifier(tableName)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (reader.FieldCount < 8)
            return null;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (rows.Values.Sum(value => value.Count) >= 512
                || reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2)
                || reader.IsDBNull(3) || reader.IsDBNull(4) || reader.IsDBNull(5)
                || reader.IsDBNull(6) || reader.IsDBNull(7))
                return null;
            var id = reader.GetInt32(0);
            if (!rows.TryGetValue(id, out var foreignKeyRows))
            {
                foreignKeyRows = [];
                rows.Add(id, foreignKeyRows);
            }
            foreignKeyRows.Add((reader.GetInt32(1), reader.GetString(3), reader.GetString(4),
                reader.GetString(2), NormalizeReferentialAction(reader.GetString(6)),
                NormalizeReferentialAction(reader.GetString(5)),
                reader.GetString(7)));
        }

        return rows.OrderBy(pair => pair.Key).Select(pair =>
        {
            var ordered = pair.Value.OrderBy(row => row.Sequence).ToArray();
            return new ActualForeignKey(ordered.Select(row => row.Column).ToArray(), ordered[0].PrincipalTable,
                ordered.Select(row => row.PrincipalColumn).ToArray(), ordered[0].OnDelete, ordered[0].OnUpdate,
                ordered[0].Match.ToUpperInvariant());
        }).ToArray();
    }

    private static bool IsActualColumnNullable(
        IReadOnlyDictionary<string, ActualColumn> actualColumns,
        ActualColumn actual)
    {
        if (actual.PrimaryKeyOrdinal == 0)
            return !actual.NotNull;

        var primaryKeyCount = actualColumns.Values.Count(column => column.PrimaryKeyOrdinal > 0);
        var isImplicitIntegerRowId = primaryKeyCount == 1
            && actual.PrimaryKeyOrdinal == 1
            && string.Equals(actual.Type, "INTEGER", StringComparison.OrdinalIgnoreCase);
        return !isImplicitIntegerRowId && !actual.NotNull;
    }

    private static bool IsImplicitIntegerRowId(
        IReadOnlyDictionary<string, ExpectedColumn> columns,
        IReadOnlyList<string> primaryKeyColumns) => primaryKeyColumns.Count == 1
            && string.Equals(columns[primaryKeyColumns[0]].Type, "INTEGER", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesSupportedTableSyntax(string tableName, string? tableSql,
        bool allowCanonicalFairnessCheck)
    {
        if (string.IsNullOrWhiteSpace(tableSql))
            return false;

        if (ContainsSqlKeyword(tableSql, "GENERATED")
            || ContainsSqlKeyword(tableSql, "DEFERRABLE")
            || ContainsSqlKeyword(tableSql, "STRICT")
            || (ContainsSqlKeyword(tableSql, "WITHOUT") && ContainsSqlKeyword(tableSql, "ROWID")))
            return false;

        var checks = ExtractCheckExpressions(tableSql);
        if (string.Equals(tableName, "collection_dispatcher_fairness_state",
                StringComparison.OrdinalIgnoreCase))
            return allowCanonicalFairnessCheck
                ? checks.Count == 1
                    && string.Equals(checks[0], "StateId=1", StringComparison.Ordinal)
                : checks.Count == 0;

        return checks.Count == 0;
    }

    private static bool ContainsSqlKeyword(string sql, string keyword)
    {
        char quote = '\0';
        for (var index = 0; index < sql.Length; index++)
        {
            var character = sql[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    if (index + 1 < sql.Length && sql[index + 1] == quote)
                        index++;
                    else
                        quote = '\0';
                }
                continue;
            }
            if (character is '\'' or '"' or '`' or '[')
            {
                quote = character == '[' ? ']' : character;
                continue;
            }
            if (index + keyword.Length <= sql.Length
                && sql.AsSpan(index, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase)
                && (index == 0 || !IsSqlIdentifierCharacter(sql[index - 1]))
                && (index + keyword.Length == sql.Length
                    || !IsSqlIdentifierCharacter(sql[index + keyword.Length])))
                return true;
        }
        return false;
    }

    private static IReadOnlyList<string> ExtractCheckExpressions(string sql)
    {
        var expressions = new List<string>();
        char quote = '\0';
        for (var index = 0; index < sql.Length; index++)
        {
            var character = sql[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    if (index + 1 < sql.Length && sql[index + 1] == quote)
                        index++;
                    else
                        quote = '\0';
                }
                continue;
            }
            if (character is '\'' or '"' or '`' or '[')
            {
                quote = character == '[' ? ']' : character;
                continue;
            }
            if (index + 5 > sql.Length
                || !sql.AsSpan(index, 5).Equals("CHECK", StringComparison.OrdinalIgnoreCase)
                || (index > 0 && IsSqlIdentifierCharacter(sql[index - 1]))
                || (index + 5 < sql.Length && IsSqlIdentifierCharacter(sql[index + 5])))
                continue;

            var open = index + 5;
            while (open < sql.Length && char.IsWhiteSpace(sql[open])) open++;
            if (open >= sql.Length || sql[open] != '(')
                return ["\u0000invalid-check-expression"];
            var depth = 0;
            var start = open + 1;
            quote = '\0';
            for (var cursor = open; cursor < sql.Length; cursor++)
            {
                var nested = sql[cursor];
                if (quote != '\0')
                {
                    if (nested == quote)
                    {
                        if (cursor + 1 < sql.Length && sql[cursor + 1] == quote)
                            cursor++;
                        else
                            quote = '\0';
                    }
                    continue;
                }
                if (nested is '\'' or '"' or '`' or '[')
                {
                    quote = nested == '[' ? ']' : nested;
                    continue;
                }
                if (nested == '(') depth++;
                else if (nested == ')' && --depth == 0)
                {
                    expressions.Add(NormalizeSqlExpression(sql[start..cursor])
                        ?? "\u0000invalid-check-expression");
                    index = cursor;
                    break;
                }
                if (cursor == sql.Length - 1)
                    return ["\u0000invalid-check-expression"];
            }
        }
        return expressions;
    }

    private static bool IsSqlIdentifierCharacter(char character) => char.IsLetterOrDigit(character)
        || character is '_' or '$';

    private static string? NormalizeExpectedDefault(object? value, string? sql)
    {
        if (sql is not null)
            return NormalizeSqlExpression(sql);
        if (value is null)
            return null;
        return value switch
        {
            bool boolean => boolean ? "1" : "0",
            string text => $"'{text.Replace("'", "''", StringComparison.Ordinal)}'",
            char character => $"'{character.ToString().Replace("'", "''", StringComparison.Ordinal)}'",
            byte[] bytes => $"X'{Convert.ToHexString(bytes)}'",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    private static string NormalizeReferentialAction(string action) => action switch
    {
        "NoAction" => "NO ACTION",
        "Restrict" => "RESTRICT",
        "Cascade" => "CASCADE",
        "SetNull" => "SET NULL",
        "SetDefault" => "SET DEFAULT",
        _ => action.ToUpperInvariant(),
    };

    private static async Task<string?> ReadIndexPredicateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string indexName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = $name;";
        command.Parameters.AddWithValue("$name", indexName);
        var sql = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (sql is null)
            return null;
        var where = sql.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
        return where < 0 ? null : NormalizeSqlExpression(sql[(where + 7)..].TrimEnd(';'));
    }

    private static string? NormalizeSqlExpression(string? expression)
    {
        if (expression is null)
            return null;

        var normalized = new StringBuilder(expression.Length);
        char quote = '\0';
        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (quote != '\0')
            {
                normalized.Append(character);
                if (character == quote)
                {
                    if (index + 1 < expression.Length && expression[index + 1] == quote)
                        normalized.Append(expression[++index]);
                    else
                        quote = '\0';
                }
                else if (quote == ']' && character == ']')
                {
                    quote = '\0';
                }
                continue;
            }

            if (character is '\'' or '"' or '`' or '[')
            {
                quote = character == '[' ? ']' : character;
                normalized.Append(character);
            }
            else if (!char.IsWhiteSpace(character))
            {
                normalized.Append(character);
            }
        }

        return quote == '\0' ? normalized.ToString() : "\u0000invalid-sql-expression";
    }

    private static async Task<CollectionSchemaHistoryLayout> ReadCollectionSchemaHistoryLayoutAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT version
            FROM collection_schema_history
            ORDER BY version
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", MaximumCollectionSchemaHistoryRows + 1);
        var versions = new List<int>(MaximumCollectionSchemaHistoryRows);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0) || versions.Count >= MaximumCollectionSchemaHistoryRows)
                return CollectionSchemaHistoryLayout.Unknown;
            var version = reader.GetInt64(0);
            if (version is < 1 or > CurrentCollectionPlatformSchemaVersion
                || (versions.Count > 0 && version != versions[^1] + 1))
                return CollectionSchemaHistoryLayout.Unknown;
            versions.Add((int)version);
        }

        if (versions.Count == 1 && versions[0] == CurrentCollectionPlatformSchemaVersion)
            return CollectionSchemaHistoryLayout.FreshEf;
        if (versions.Count == 0 || versions[^1] != CurrentCollectionPlatformSchemaVersion)
            return CollectionSchemaHistoryLayout.Unknown;
        return versions[0] switch
        {
            6 => CollectionSchemaHistoryLayout.UpgradeV6,
            18 => CollectionSchemaHistoryLayout.UpgradeV18,
            _ => CollectionSchemaHistoryLayout.Upgrade,
        };
    }

    private static async Task<bool> MatchesEventStoreMigrationHistoryAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var expected = EventStoreMigrationIds.Value;
        if (expected.Count == 0 || expected.Count > MaximumMigrationHistoryRows)
            return false;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = """
            SELECT CASE WHEN length(CAST(MigrationId AS BLOB)) <= 512
                        THEN MigrationId ELSE NULL END
            FROM __EFMigrationsHistory
            ORDER BY MigrationId
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Min(expected.Count + 1, MaximumMigrationHistoryRows + 1));
        var actual = new List<string>(expected.Count);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (actual.Count >= MaximumMigrationHistoryRows || reader.IsDBNull(0))
                return false;
            actual.Add(reader.GetString(0));
        }

        var valid = actual.SequenceEqual(expected, StringComparer.Ordinal);
        return valid;
    }

    private static async Task<IReadOnlyList<ActualIndexColumn>> ReadIndexColumnsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string indexName,
        CancellationToken cancellationToken)
    {
        var columns = new List<ActualIndexColumn>(16);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = $"PRAGMA index_xinfo({QuoteIdentifier(indexName)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (reader.FieldCount < 6)
        {
            return [];
        }
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (columns.Count >= 16 || reader.IsDBNull(5))
                return [];
            if (reader.GetInt32(5) == 0)
                continue;
            if (reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3) || reader.IsDBNull(4))
                return [];
            columns.Add(new(reader.GetString(2), reader.GetInt32(3) != 0, reader.GetString(4)));
        }
        return columns;
    }

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Fingerprint(IEnumerable<SchemaObject> objects)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var item in objects.OrderBy(item => item.Type, StringComparer.Ordinal)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            Append(hash, item.Type);
            Append(hash, item.Name);
            Append(hash, item.Sql ?? "oversized-schema-sql");
            Append(hash, item.SqlBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    internal static async Task<SqliteConnection> OpenReadOnlyAsync(string path, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = CommandTimeoutSeconds,
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = "PRAGMA query_only = ON;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<IReadOnlyList<MigrationRootProbe>> InspectRootsAsync(
        string eventStorePath, string collectionPath, IReadOnlyList<HorseIdentityMigrationMapping> mappings,
        CancellationToken cancellationToken)
    {
        await using var eventConnection = await OpenReadOnlyAsync(eventStorePath, cancellationToken).ConfigureAwait(false);
        await using var eventTransaction = eventConnection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
        var identifiers = mappings.SelectMany(mapping => new[] { mapping.OldHorseId, mapping.DestinationHorseId })
            .Distinct(StringComparer.Ordinal).ToArray();

        var horseRoots = await ReadIdentifierCountsAsync(eventConnection, eventTransaction,
            "SELECT HorseId, COUNT(*) FROM Horses WHERE HorseId IN ({ids}) GROUP BY HorseId LIMIT $limit;", identifiers,
            cancellationToken).ConfigureAwait(false);
        var horseStreams = await ReadIdentifierCountsAsync(eventConnection, eventTransaction,
            "SELECT AggregateId, COUNT(*) FROM EventEntity WHERE AggregateName = $aggregate AND AggregateId IN ({ids}) GROUP BY AggregateId LIMIT $limit;",
            identifiers, cancellationToken, ("$aggregate", "HorseAggregate")).ConfigureAwait(false);
        var firstEvents = await ReadIdentifierCountsAsync(eventConnection, eventTransaction,
            "SELECT AggregateId, COUNT(*) FROM EventEntity WHERE AggregateName = $aggregate AND AggregateSequenceNumber = 1 AND AggregateId IN ({ids}) GROUP BY AggregateId LIMIT $limit;",
            identifiers, cancellationToken, ("$aggregate", "HorseAggregate")).ConfigureAwait(false);
        var snapshots = await ReadIdentifierCountsAsync(eventConnection, eventTransaction,
            "SELECT AggregateId, COUNT(*) FROM SnapshotEntity WHERE AggregateName = $aggregate AND AggregateId IN ({ids}) GROUP BY AggregateId LIMIT $limit;",
            identifiers, cancellationToken, ("$aggregate", "HorseAggregate")).ConfigureAwait(false);
        await eventTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        await using var collectionConnection = await OpenReadOnlyAsync(collectionPath, cancellationToken).ConfigureAwait(false);
        await using var collectionTransaction = collectionConnection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
        var resources = await ReadIdentifierCountsAsync(collectionConnection, collectionTransaction,
            "SELECT ResourceId, COUNT(*) FROM collection_resources WHERE Type = 'Horse' AND Provider = 'JRA' AND ResourceId IN ({ids}) GROUP BY ResourceId LIMIT $limit;",
            identifiers, cancellationToken).ConfigureAwait(false);
        var suppressions = await ReadIdentifierCountsAsync(collectionConnection, collectionTransaction,
            "SELECT ResourceId, COUNT(*) FROM collection_resource_suppressions WHERE Type = 'Horse' AND Provider = 'JRA' AND ResourceId IN ({ids}) GROUP BY ResourceId LIMIT $limit;",
            identifiers, cancellationToken).ConfigureAwait(false);
        var selections = await ReadIdentifierCountsAsync(collectionConnection, collectionTransaction,
            "SELECT ResourceId, COUNT(*) FROM collection_subject_identification_selections WHERE ResourceType = 'Horse' AND Provider = 'JRA' AND ResourceId IN ({ids}) GROUP BY ResourceId LIMIT $limit;",
            identifiers, cancellationToken).ConfigureAwait(false);
        var failures = await ReadIdentifierCountsAsync(collectionConnection, collectionTransaction,
            "SELECT CanonicalResourceId, COUNT(*) FROM collection_failure_notifications WHERE CanonicalResourceType = 'Horse' AND CanonicalResourceProvider = 'JRA' AND CanonicalResourceId IN ({ids}) GROUP BY CanonicalResourceId LIMIT $limit;",
            identifiers, cancellationToken).ConfigureAwait(false);
        await collectionTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return mappings.Select(mapping =>
        {
            var oldHorseRootCount = horseRoots.GetValueOrDefault(mapping.OldHorseId);
            var oldFirstEventCount = firstEvents.GetValueOrDefault(mapping.OldHorseId);
            var sourceExists = oldHorseRootCount > 0 && horseStreams.ContainsKey(mapping.OldHorseId)
                && oldFirstEventCount > 0;
            var duplicateSource = oldHorseRootCount > 1 || oldFirstEventCount > 1
                || resources.GetValueOrDefault(mapping.OldHorseId) > 1
                || suppressions.GetValueOrDefault(mapping.OldHorseId) > 1;
            var hasCollision = horseRoots.ContainsKey(mapping.DestinationHorseId)
                || horseStreams.ContainsKey(mapping.DestinationHorseId)
                || snapshots.ContainsKey(mapping.DestinationHorseId)
                || (resources.ContainsKey(mapping.OldHorseId)
                    && resources.ContainsKey(mapping.DestinationHorseId))
                || (suppressions.ContainsKey(mapping.OldHorseId)
                    && suppressions.ContainsKey(mapping.DestinationHorseId));
            var status = hasCollision ? MigrationRootStatus.TargetCollision
                : duplicateSource ? MigrationRootStatus.Duplicate
                : !sourceExists ? MigrationRootStatus.Missing
                : MigrationRootStatus.Present;
            return new MigrationRootProbe(mapping.OldHorseId, mapping.DestinationHorseId, status,
                horseRoots.ContainsKey(mapping.DestinationHorseId), horseStreams.ContainsKey(mapping.DestinationHorseId),
                snapshots.ContainsKey(mapping.DestinationHorseId), resources.ContainsKey(mapping.DestinationHorseId),
                suppressions.ContainsKey(mapping.DestinationHorseId), selections.ContainsKey(mapping.DestinationHorseId),
                failures.ContainsKey(mapping.DestinationHorseId),
                resources.ContainsKey(mapping.OldHorseId) && resources.ContainsKey(mapping.DestinationHorseId),
                suppressions.ContainsKey(mapping.OldHorseId) && suppressions.ContainsKey(mapping.DestinationHorseId));
        }).ToArray();
    }

    private static async Task<Dictionary<string, long>> ReadIdentifierCountsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string query,
        IReadOnlyList<string> identifiers, CancellationToken cancellationToken,
        (string Name, string Value)? extraParameter = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        var parameters = identifiers.Select((_, index) => $"$id{index}").ToArray();
        command.CommandText = query.Replace("{ids}", string.Join(",", parameters), StringComparison.Ordinal);
        command.Parameters.AddWithValue("$limit", identifiers.Count + 1);
        for (var index = 0; index < identifiers.Count; index++)
            command.Parameters.AddWithValue(parameters[index], identifiers[index]);
        if (extraParameter is { } extra)
            command.Parameters.AddWithValue(extra.Name, extra.Value);

        var results = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            if (!reader.IsDBNull(0) && !reader.IsDBNull(1)) results[reader.GetString(0)] = reader.GetInt64(1);
        return results;
    }

    private sealed record ConfiguredStore(
        MigrationStoreSurface Surface,
        string? Path,
        bool Required,
        string[] ExpectedTables,
        string[] MetadataTables);

    private sealed record ExpectedTable(
        IReadOnlyDictionary<string, ExpectedColumn> Columns,
        IReadOnlyDictionary<string, ExpectedIndex> Indexes,
        IReadOnlyList<ExpectedForeignKey>? ForeignKeys = null);

    private sealed record ExpectedColumn(string Type, bool Nullable, int PrimaryKeyOrdinal,
        string? DefaultExpression = null);

    private sealed record ExpectedForeignKey(
        IReadOnlyList<string> Columns,
        string PrincipalTable,
        IReadOnlyList<string> PrincipalColumns,
        string OnDelete,
        string OnUpdate);

    private sealed record ExpectedIndex(IReadOnlyList<string> Columns, bool Unique, string? Predicate);

    private sealed record ActualColumn(string Type, bool NotNull, string? DefaultValue, int PrimaryKeyOrdinal);

    private sealed record ActualForeignKey(
        IReadOnlyList<string> Columns,
        string PrincipalTable,
        IReadOnlyList<string> PrincipalColumns,
        string OnDelete,
        string OnUpdate,
        string Match);

    private sealed record ActualIndex(bool Unique, bool Partial, string Origin, string? Predicate);

    private sealed record ActualIndexColumn(string Name, bool Descending, string Collation);

    private sealed record SchemaScan(IReadOnlyList<SchemaObject> Objects, bool Truncated);

    private sealed record SchemaObject(string Type, string Name, string? Sql, long SqlBytes);

    private enum CollectionSchemaHistoryLayout
    {
        Unknown,
        FreshEf,
        Upgrade,
        UpgradeV6,
        UpgradeV18,
    }
}
