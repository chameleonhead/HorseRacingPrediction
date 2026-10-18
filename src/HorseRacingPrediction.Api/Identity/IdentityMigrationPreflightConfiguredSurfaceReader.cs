using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Api.IdentityMigration;
using HorseRacingPrediction.PredictionScheduling;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Identity;

internal sealed class IdentityMigrationPreflightConfiguredSurfaceReader(
    IOptions<CollectionPlatformOptions> collectionOptions,
    IOptions<PredictionScheduleOptions> scheduleOptions,
    IOptions<CollectionQueueOptions> queueOptions,
    IWebHostEnvironment environment)
{
    private const int ReadTimeoutSeconds = 1;
    private const int MaximumSchemaObjects = 4096;
    private const int MaximumSchemaSqlBytes = 8192;
    private const int MaximumPayloadBytes = IdentityMigrationPreflightPolicy.MaximumPayloadBytes;
    private const int MaximumJsonDepth = IdentityMigrationPreflightPolicy.MaximumJsonDepth;
    private const int MaximumRelatedSources = 16;
    private const int MaximumRelatedSourcesOverfetch = MaximumRelatedSources + 1;
    private static readonly string[] KnownCollectionPlatformMigrationTables =
    [
        "collection_schema_history", "collection_platform_controls", "collection_failure_notifications",
        "collection_backfill_batches", "collection_resource_suppressions", "collection_request_batch_bindings",
        "collection_execution_leases", "race_artifact_states", "race_scheduling_evidence",
        "collection_attempt_stage_outcomes", "collection_request_batch_bindings_v18", "race_repair_holds",
        "collection_dispatcher_fairness_state", "collection_subject_identification_selections",
        "collection_batch_recovery_progress", "collection_batch_recovery_scan",
    ];
    private static readonly string[] KnownCollectionPlatformMigrationIndexes =
    [
        "IX_collection_failure_notifications_PublishedAt_AvailableAt",
        "IX_collection_failure_notifications_TaskId",
        "IX_collection_backfill_batches_Provider_From_To",
        "IX_collection_failure_notifications_ResolutionStatus_AvailableAt",
        "IX_collection_failure_notifications_RecoveryTaskId",
        "IX_collection_task_outbox_DispatchedAt_ReservedUntilUnixMilliseconds",
        "IX_collection_attempts_ExecutionBatchId",
        "IX_collection_tasks_ResourcePk_DefinitionId_RequestedRevision_CreatedAt",
        "IX_collection_resource_suppressions_RepairId",
        "IX_collection_request_batch_bindings_RequestId",
        "IX_collection_execution_leases_Status_LeaseExpiresAt",
        "IX_race_artifact_states_Status_NextDueAt",
        "IX_collection_subject_identification_selections_CanonicalTaskId",
        "IX_collection_task_outbox_TaskId_DispatchGeneration_DispatchedAt",
        "IX_collection_states_NextCollectionAt",
        "IX_collection_batch_recovery_progress_Kind_LastVisitedSequence_BatchId",
        "IX_collection_attempt_stage_outcomes_AttemptId_Stage",
        "IX_race_repair_holds_OperationId",
        "IX_race_repair_holds_RaceId",
    ];

    private HorseIdentityMigrationConfiguredSources ResolveConfiguredPaths() =>
        HorseIdentityMigrationConfiguredSources.Resolve(null, collectionOptions.Value, scheduleOptions.Value,
            queueOptions.Value, environment.ContentRootPath, Environment.CurrentDirectory, AppContext.BaseDirectory);

    internal async Task<IReadOnlyList<IdentityMigrationPreflightSchemaObservation>> InspectAsync(
        CancellationToken cancellationToken)
    {
        var observations = new List<IdentityMigrationPreflightSchemaObservation>(3);
        var paths = ResolveConfiguredPaths();

        var platformPath = paths.CollectionPlatformPath
            ?? throw new InvalidOperationException("The configured collection store path is invalid.");
        var platformKnown = GetCollectionPlatformTableNames(platformPath);
        observations.Add(await IdentityMigrationPreflightSqliteSchema.InspectAsync(
            IdentityMigrationPreflightSurface.CollectionPlatform,
            platformPath,
            platformKnown,
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["collection_resources"] = ["ResourcePk", "Type", "Provider", "ResourceId", "AttributesJson"],
            },
            ["collection_resources"], cancellationToken).ConfigureAwait(false));

        var schedulePath = paths.PredictionSchedulePath
            ?? throw new InvalidOperationException("The configured schedule store path is invalid.");
        observations.Add(await IdentityMigrationPreflightSqliteSchema.InspectAsync(
            IdentityMigrationPreflightSurface.PredictionSchedule,
            schedulePath,
            ["prediction_candidates"],
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["prediction_candidates"] = ["RaceId", "Status", "AvailableAt", "LeaseToken", "LeaseExpiresAt"],
            },
            ["prediction_candidates", "IX_prediction_candidates_Status_AvailableAt"], cancellationToken).ConfigureAwait(false));

        if (paths.LocalCollectionQueuePath is not { } queuePath)
        {
            observations.Add(IdentityMigrationPreflightSchemaObservation.NotConfigured(
                IdentityMigrationPreflightSurface.LocalCollectionQueue));
        }
        else
        {
            observations.Add(await IdentityMigrationPreflightSqliteSchema.InspectAsync(
                IdentityMigrationPreflightSurface.LocalCollectionQueue,
                queuePath,
                ["local_collection_messages"],
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["local_collection_messages"] = ["message_id", "body", "visible_at", "created_at"],
                },
                ["local_collection_messages"], cancellationToken).ConfigureAwait(false));
        }

        return observations;
    }

    internal async Task<IdentityMigrationPreflightCollectionPage> ReadCollectionResourcesAsync(
        string? afterKey, CancellationToken cancellationToken)
    {
        var path = ResolveConfiguredPaths().CollectionPlatformPath
            ?? throw new InvalidOperationException("The configured collection store path is invalid.");
        await using var connection = await IdentityMigrationPreflightSqliteSchema.OpenReadOnlyAsync(path,
            cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);

        var candidates = new List<(string? Key, long Bytes)>(IdentityMigrationPreflightPolicy.PageSize + 1);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandTimeout = ReadTimeoutSeconds;
            command.CommandText = """
                SELECT CASE WHEN length(CAST(ResourceId AS BLOB)) <= $maxKeyBytes THEN ResourceId ELSE NULL END,
                       length(CAST(ResourceId AS BLOB))
                FROM collection_resources
                WHERE Type = 'Horse' AND Provider = 'JRA'
                  AND ($after IS NULL OR ResourceId COLLATE BINARY > $after)
                GROUP BY ResourceId COLLATE BINARY
                ORDER BY ResourceId COLLATE BINARY
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$after", (object?)afterKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$maxKeyBytes", IdentityMigrationPreflightPolicy.MaximumKeyCharacters * 4);
            command.Parameters.AddWithValue("$limit", IdentityMigrationPreflightPolicy.PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                candidates.Add((reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt64(1)));
        }

        var hasMore = candidates.Count > IdentityMigrationPreflightPolicy.PageSize;
        if (hasMore) candidates.RemoveAt(candidates.Count - 1);
        var overlongCount = candidates.Count(candidate => candidate.Key is null
            || candidate.Key.Length > IdentityMigrationPreflightPolicy.MaximumKeyCharacters
            || candidate.Bytes > IdentityMigrationPreflightPolicy.MaximumKeyCharacters * 4);
        var keys = candidates.Where(candidate => candidate.Key is not null
                && candidate.Key.Length <= IdentityMigrationPreflightPolicy.MaximumKeyCharacters
                && candidate.Bytes <= IdentityMigrationPreflightPolicy.MaximumKeyCharacters * 4)
            .Select(candidate => candidate.Key!).ToList();
        var sources = keys.Count == 0
            ? new Dictionary<string, IdentityMigrationPreflightCollectionSource>(StringComparer.Ordinal)
            : await ReadCollectionSourcesAsync(connection, (SqliteTransaction)transaction, keys,
                cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(keys, sources, hasMore && overlongCount == 0, overlongCount > 0, overlongCount);
    }

    private static async Task<Dictionary<string, IdentityMigrationPreflightCollectionSource>> ReadCollectionSourcesAsync(
        SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var parameters = keys.Select((_, index) => "$key" + index).ToArray();
        var parameterList = string.Join(",", parameters);
        var stateByKey = keys.ToDictionary(key => key, _ => new CollectionSourceAccumulator(), StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = ReadTimeoutSeconds;
        command.CommandText = $"""
            WITH bounded AS (
                SELECT ResourceId,
                       CASE WHEN length(CAST(AttributesJson AS BLOB)) <= $maxBytes THEN AttributesJson ELSE NULL END AS AttributesJson,
                       length(CAST(AttributesJson AS BLOB)) AS PayloadBytes,
                       ROW_NUMBER() OVER (PARTITION BY ResourceId ORDER BY ResourcePk) AS RowNumber
                FROM collection_resources
                WHERE Type = 'Horse' AND Provider = 'JRA' AND ResourceId IN ({parameterList})
            )
            SELECT ResourceId, AttributesJson, PayloadBytes
            FROM bounded WHERE RowNumber <= $sourceLimit
            ORDER BY ResourceId COLLATE BINARY, RowNumber;
            """;
        command.Parameters.AddWithValue("$maxBytes", MaximumPayloadBytes);
        command.Parameters.AddWithValue("$sourceLimit", MaximumRelatedSourcesOverfetch);
        for (var index = 0; index < keys.Count; index++)
            command.Parameters.AddWithValue(parameters[index], keys[index]);

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = reader.GetString(0);
                if (!stateByKey.TryGetValue(key, out var state)) continue;
                state.RowCount++;
                var bytes = reader.IsDBNull(2) ? long.MaxValue : reader.GetInt64(2);
                if (state.RowCount > MaximumRelatedSources)
                {
                    state.Truncated = true;
                    continue;
                }
                if (bytes > MaximumPayloadBytes || reader.IsDBNull(1))
                {
                    state.Truncated = true;
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(reader.GetString(1),
                        new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        state.Unknown = true;
                        continue;
                    }

                    var sourceIdentity = ReadOptionalString(document.RootElement, "sourceIdentity", out var identityShape);
                    var sourceUrl = ReadOptionalString(document.RootElement, "sourceUrl", out var urlShape);
                    if (!identityShape || !urlShape)
                    {
                        state.Unknown = true;
                        continue;
                    }

                    var canonicalIdentity = NormalizeIdentity(sourceIdentity, sourceUrl, out var validShape);
                    if (!validShape)
                    {
                        state.Unknown = true;
                        continue;
                    }
                    if (canonicalIdentity is not null) state.Identities.Add(canonicalIdentity);
                }
                catch (JsonException)
                {
                    state.Unknown = true;
                }
            }
        }

        var output = new Dictionary<string, IdentityMigrationPreflightCollectionSource>(StringComparer.Ordinal);
        foreach (var (key, state) in stateByKey)
        {
            if (state.RowCount == 0)
            {
                output[key] = new(null, IdentityMigrationPreflightObservationStatus.Unverifiable, false);
                continue;
            }

            var status = state.Unknown
                ? IdentityMigrationPreflightObservationStatus.UnknownShape
                : state.Truncated
                    ? IdentityMigrationPreflightObservationStatus.Truncated
                    : state.Identities.Count > 1
                        ? IdentityMigrationPreflightObservationStatus.Conflict
                        : state.Identities.Count == 1
                            ? IdentityMigrationPreflightObservationStatus.Observed
                            : IdentityMigrationPreflightObservationStatus.Unverifiable;
            output[key] = new(state.Identities.Count == 1 ? state.Identities.Single() : null,
                status, state.Truncated);
        }

        return output;
    }

    private static string? NormalizeIdentity(string? identity, string? sourceUrl, out bool validShape)
    {
        validShape = true;
        var identityValue = string.IsNullOrWhiteSpace(identity) ? null : identity;
        var urlValue = string.IsNullOrWhiteSpace(sourceUrl) ? null : sourceUrl;
        string? fromIdentity = null;
        string? fromUrl = null;
        if (identityValue is not null)
        {
            if (IdentityMigrationPreflightPolicy.IsCanonicalJraIdentity(identityValue))
                fromIdentity = identityValue;
            else if (TryNormalizeJraUrl(identityValue, out var normalized)) fromIdentity = normalized;
            else validShape = false;
        }
        if (urlValue is not null)
        {
            if (!TryNormalizeJraUrl(urlValue, out fromUrl)) validShape = false;
        }

        if (!validShape) return null;
        if (fromIdentity is not null && fromUrl is not null
            && !string.Equals(fromIdentity, fromUrl, StringComparison.Ordinal))
        {
            validShape = false;
            return null;
        }
        return fromIdentity ?? fromUrl;
    }

    private static bool TryNormalizeJraUrl(string value, out string? identity)
    {
        identity = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("www.jra.go.jp", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
            || !JraSourceIdentity.TryNormalizeHorse(value, out var normalized)
            || !IdentityMigrationPreflightPolicy.IsCanonicalJraIdentity(normalized))
            return false;
        identity = normalized;
        return true;
    }

    private static string? ReadOptionalString(JsonElement root, string name, out bool valid)
    {
        valid = true;
        var found = false;
        string? result = null;
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (found || property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                valid = false;
                return null;
            }
            found = true;
            result = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        }
        return result;
    }

    private static IReadOnlyList<string> GetCollectionPlatformTableNames(string path)
    {
        var options = new DbContextOptionsBuilder<CollectionPlatformDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=1").Options;
        using var context = new CollectionPlatformDbContext(options);
        return context.Model.GetEntityTypes().Select(entity => entity.GetTableName())
            .Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>()
            .Concat(context.Model.GetEntityTypes().SelectMany(entity => entity.GetIndexes())
                .Select(index => index.GetDatabaseName()).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>())
            .Concat(KnownCollectionPlatformMigrationTables)
            .Concat(KnownCollectionPlatformMigrationIndexes)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private sealed class CollectionSourceAccumulator
    {
        internal int RowCount { get; set; }
        internal bool Unknown { get; set; }
        internal bool Truncated { get; set; }
        internal HashSet<string> Identities { get; } = new(StringComparer.Ordinal);
    }
}

internal sealed record IdentityMigrationPreflightCollectionPage(
    IReadOnlyList<string> Keys,
    IReadOnlyDictionary<string, IdentityMigrationPreflightCollectionSource> Sources,
    bool HasMore,
    bool NoSafeContinuation,
    int OverlongCount);

internal sealed record IdentityMigrationPreflightCollectionSource(
    string? CanonicalIdentity,
    IdentityMigrationPreflightObservationStatus Status,
    bool Truncated);

internal sealed record IdentityMigrationPreflightSchemaObservation(
    IdentityMigrationPreflightSurface Surface,
    IdentityMigrationPreflightObservationStatus Status,
    DateTimeOffset? ObservedAtUtc,
    int KnownObjectCount,
    int UnknownObjectCount,
    string AssumptionFingerprint)
{
    internal IdentityMigrationPreflightSurfaceObservation ToContract() => new(
        Surface, Status, ObservedAtUtc, KnownObjectCount, UnknownObjectCount);

    internal static IdentityMigrationPreflightSchemaObservation NotConfigured(IdentityMigrationPreflightSurface surface) =>
        new(surface, IdentityMigrationPreflightObservationStatus.NotConfigured, null, 0, 0, "not-configured");
}

internal static class IdentityMigrationPreflightSqliteSchema
{
    private const int TimeoutSeconds = 1;
    private const int MaxObjects = 4096;
    private const int MaximumSchemaSqlBytes = 8192;
    private static readonly AsyncLocal<Action<string>?> StatementObserver = new();
    private static readonly SQLitePCL.strdelegate_trace TraceCallback = (_, statement) =>
        StatementObserver.Value?.Invoke(statement);

    internal static IDisposable ObserveStatements(Action<string> observer)
    {
        var previous = StatementObserver.Value;
        StatementObserver.Value = observer;
        return new RestoreObserver(() => StatementObserver.Value = previous);
    }

    internal static async Task<IdentityMigrationPreflightSchemaObservation> InspectAsync(
        IdentityMigrationPreflightSurface surface, string path, IReadOnlyCollection<string> knownNames,
        IReadOnlyDictionary<string, string[]> expectedColumns, IReadOnlyCollection<string> requiredNames,
        CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        if (!File.Exists(path))
            return new(surface, IdentityMigrationPreflightObservationStatus.Unavailable, observedAt, 0, 0,
                "missing");

        try
        {
            await using var connection = await OpenReadOnlyAsync(path, cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
            var objects = new List<(string Type, string Name, string? Sql, long SqlBytes)>(MaxObjects + 1);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandTimeout = TimeoutSeconds;
                command.CommandText = """
                    SELECT type, name,
                           CASE WHEN length(CAST(sql AS BLOB)) <= $maxSqlBytes THEN sql ELSE NULL END,
                           length(CAST(sql AS BLOB))
                    FROM sqlite_master
                    WHERE type IN ('table', 'view', 'index', 'trigger') AND name NOT LIKE 'sqlite_%'
                    ORDER BY type, name LIMIT $limit;
                    """;
                command.Parameters.AddWithValue("$limit", MaxObjects + 1);
                command.Parameters.AddWithValue("$maxSqlBytes", MaximumSchemaSqlBytes);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    objects.Add((reader.GetString(0), reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? 0 : reader.GetInt64(3)));
            }

            var truncated = objects.Count > MaxObjects || objects.Any(item => item.SqlBytes > MaximumSchemaSqlBytes);
            if (objects.Count > MaxObjects) objects.RemoveAt(objects.Count - 1);
            var known = new HashSet<string>(knownNames, StringComparer.OrdinalIgnoreCase);
            var knownCount = objects.Count(item => known.Contains(item.Name));
            var unknownCount = objects.Count - knownCount;
            var presentTables = objects.Where(item => item.Type == "table")
                .Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unsupported = requiredNames.Any(name => !presentTables.Contains(name));
            var (fingerprint, missingRequiredColumn) = await FingerprintAsync(connection,
                (SqliteTransaction)transaction, objects, expectedColumns, cancellationToken).ConfigureAwait(false);
            unsupported |= missingRequiredColumn;
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            var status = truncated
                ? IdentityMigrationPreflightObservationStatus.Truncated
                : unsupported
                    ? IdentityMigrationPreflightObservationStatus.Unsupported
                    : unknownCount > 0
                        ? IdentityMigrationPreflightObservationStatus.UnknownShape
                        : IdentityMigrationPreflightObservationStatus.Observed;
            return new(surface, status, observedAt, knownCount, unknownCount, fingerprint);
        }
        catch (SqliteException)
        {
            return new(surface, IdentityMigrationPreflightObservationStatus.Unavailable, observedAt, 0, 0,
                "unavailable");
        }
    }

    internal static async Task<SqliteConnection> OpenReadOnlyAsync(string path, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = TimeoutSeconds,
        }.ConnectionString;
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (StatementObserver.Value is not null)
                SQLitePCL.raw.sqlite3_trace(connection.Handle, TraceCallback, null);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = TimeoutSeconds;
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

    private static async Task<(string Fingerprint, bool MissingRequiredColumn)> FingerprintAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<(string Type, string Name, string? Sql, long SqlBytes)> objects,
        IReadOnlyDictionary<string, string[]> expectedColumns, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var missingRequiredColumn = false;
        foreach (var item in objects)
        {
            Append(hash, item.Type);
            Append(hash, item.Name);
            Append(hash, item.Sql ?? "oversized-schema-definition");
        }

        foreach (var table in expectedColumns.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var columns = new List<(string Name, string Type)>();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = TimeoutSeconds;
            command.CommandText = $"PRAGMA table_info(\"{table.Key.Replace("\"", "\"\"", StringComparison.Ordinal)}\");";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                columns.Add((reader.GetString(1), reader.IsDBNull(2) ? string.Empty : reader.GetString(2)));
            Append(hash, table.Key);
            foreach (var column in columns.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
            {
                Append(hash, column.Name);
                Append(hash, column.Type);
            }
            if (table.Value.Any(expected => !columns.Any(column =>
                    column.Name.Equals(expected, StringComparison.OrdinalIgnoreCase))))
            {
                Append(hash, "missing-required-column");
                missingRequiredColumn = true;
            }
        }
        return (Convert.ToHexString(hash.GetHashAndReset()), missingRequiredColumn);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private sealed class RestoreObserver(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
