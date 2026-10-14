using System.Data;
using System.Security.Cryptography;
using System.Text;
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
    bool TypedReferenceClosureInspected);

/// <summary>
/// Reads configured SQLite surfaces without resolving normal store constructors or triggering schema setup.
/// This first slice deliberately never claims full reference closure and never exposes a writer.
/// </summary>
internal sealed class TypedHorseIdentityStoreReaders(
    IConfiguration configuration,
    IOptions<CollectionPlatformOptions> collectionOptions,
    IOptions<PredictionScheduleOptions> scheduleOptions,
    IOptions<CollectionQueueOptions> queueOptions,
    IWebHostEnvironment environment)
{
    private const int CommandTimeoutSeconds = 1;
    private const int SchemaObjectPageSize = 128;
    private const int MaximumSchemaObjects = 512;
    private const int MaximumSchemaSqlBytes = 8192;
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
            return new(schemas, [], TypedReferenceClosureInspected: false);

        var eventStore = configured.Single(store => store.Surface == MigrationStoreSurface.EventStore);
        var collection = configured.Single(store => store.Surface == MigrationStoreSurface.CollectionPlatform);
        var roots = await InspectRootsAsync(eventStore.Path!, collection.Path!, mappings, cancellationToken)
            .ConfigureAwait(false);

        // T7a does not inspect the typed read-model/event/reference closure. A successful root probe is
        // still explicitly incomplete and cannot authorize a source write.
        return new(schemas, roots, TypedReferenceClosureInspected: false);
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

    private static IReadOnlyDictionary<string, ExpectedTable> BuildExpectedEventStoreModel()
    {
        var model = new Dictionary<string, ExpectedTable>(
            BuildExpectedModel(CreateEventStoreModel()), StringComparer.OrdinalIgnoreCase);
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
                        Array.IndexOf(primaryKeyColumns.ToArray(), column) + 1),
                    StringComparer.OrdinalIgnoreCase);
                var indexes = table.Indexes.ToDictionary(index => index.Name,
                    index => new ExpectedIndex(index.Columns.Select(column => column.Name).ToArray(),
                        index.IsUnique, index.Filter),
                    StringComparer.OrdinalIgnoreCase);
                tables.Add(table.Name, new(columns, indexes));
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
                        ["receive_count"] = new("INTEGER", false, 0),
                        ["dead_letter"] = new("INTEGER", false, 0),
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

            var expectedShapes = GetExpectedTables(store.Surface);
            var expectedTables = expectedShapes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var acceptedMetadataTables = MetadataTableShapes(store.Surface);
            var tables = objects.Where(item => item.Type == "table").Select(item => item.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            var malformedSchema = unknownObjects > 0
                || store.MetadataTables.Any(table => !tables.Contains(table));
            foreach (var tableName in tables)
            {
                var expected = expectedShapes.TryGetValue(tableName, out var modelShape)
                    ? modelShape
                    : acceptedMetadataTables.GetValueOrDefault(tableName);
                if (expected is null || !await MatchesTableShapeAsync(connection, transaction,
                        tableName, expected, cancellationToken).ConfigureAwait(false))
                {
                    malformedSchema = true;
                }
            }
            if (store.Surface == MigrationStoreSurface.CollectionPlatform
                && tables.Contains("collection_schema_history", StringComparer.OrdinalIgnoreCase)
                && !await MatchesCollectionSchemaHistoryAsync(connection, transaction, cancellationToken)
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
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<bool> MatchesTableShapeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        ExpectedTable expected,
        CancellationToken cancellationToken)
    {
        var actualColumns = new Dictionary<string, ActualColumn>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (actualColumns.Count >= 512)
                    return false;
                actualColumns[reader.GetString(1)] = new(
                    reader.GetString(2), reader.GetInt32(3) != 0, reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt32(5));
            }
        }

        if (actualColumns.Count != expected.Columns.Count)
            return false;
        foreach (var (name, expectedColumn) in expected.Columns)
        {
            if (!actualColumns.TryGetValue(name, out var actual)
                || !string.Equals(actual.Type, expectedColumn.Type, StringComparison.OrdinalIgnoreCase)
                || actual.PrimaryKeyOrdinal != expectedColumn.PrimaryKeyOrdinal
                || IsActualColumnNullable(actualColumns, actual) != expectedColumn.Nullable)
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
                var name = reader.GetString(1);
                if (name.StartsWith("sqlite_autoindex_", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (actualIndexes.Count >= 128)
                    return false;
                actualIndexes[name] = new(reader.GetInt32(2) != 0, reader.GetInt32(4) != 0,
                    await ReadIndexPredicateAsync(connection, transaction, name, cancellationToken)
                        .ConfigureAwait(false));
            }
        }

        if (actualIndexes.Count != expected.Indexes.Count
            || !actualIndexes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(expected.Indexes.Keys))
            return false;

        foreach (var (indexName, expectedIndex) in expected.Indexes)
        {
            var actualIndex = actualIndexes[indexName];
            if (actualIndex.Unique != expectedIndex.Unique
                || actualIndex.Partial != (expectedIndex.Predicate is not null)
                || !string.Equals(actualIndex.Predicate, NormalizeSqlExpression(expectedIndex.Predicate),
                    StringComparison.OrdinalIgnoreCase))
                return false;
            var actualIndexColumns = await ReadIndexColumnsAsync(connection, transaction, indexName, cancellationToken)
                .ConfigureAwait(false);
            if (!actualIndexColumns.SequenceEqual(expectedIndex.Columns, StringComparer.OrdinalIgnoreCase))
                return false;
        }
        return true;
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

    private static string? NormalizeSqlExpression(string? expression) => expression is null
        ? null
        : string.Concat(expression.Where(character => !char.IsWhiteSpace(character)));

    private static async Task<bool> MatchesCollectionSchemaHistoryAsync(
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
        var count = 0;
        var previous = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0)) return false;
            var version = reader.GetInt32(0);
            if (version <= previous || version is < 1 or > CurrentCollectionPlatformSchemaVersion)
                return false;
            previous = version;
            count++;
            if (count > MaximumCollectionSchemaHistoryRows) return false;
        }

        return count > 0 && previous == CurrentCollectionPlatformSchemaVersion;
    }

    private static async Task<IReadOnlyList<string>> ReadIndexColumnsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string indexName,
        CancellationToken cancellationToken)
    {
        var columns = new List<string>(16);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = $"PRAGMA index_info({QuoteIdentifier(indexName)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (columns.Count >= 16 || reader.IsDBNull(2))
                return [];
            columns.Add(reader.GetString(2));
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
        IReadOnlyDictionary<string, ExpectedIndex> Indexes);

    private sealed record ExpectedColumn(string Type, bool Nullable, int PrimaryKeyOrdinal);

    private sealed record ExpectedIndex(IReadOnlyList<string> Columns, bool Unique, string? Predicate);

    private sealed record ActualColumn(string Type, bool NotNull, string? DefaultValue, int PrimaryKeyOrdinal);

    private sealed record ActualIndex(bool Unique, bool Partial, string? Predicate);

    private sealed record SchemaScan(IReadOnlyList<SchemaObject> Objects, bool Truncated);

    private sealed record SchemaObject(string Type, string Name, string? Sql, long SqlBytes);
}
