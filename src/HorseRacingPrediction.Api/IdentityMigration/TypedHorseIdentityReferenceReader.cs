using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventFlow.Aggregates;
using EventFlow.EventStores;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common.Time;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Domain.Memos;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Domain.Trainers;
using HorseRacingPrediction.Infrastructure.Persistence.HorseIdentityMigration;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal enum TypedHorseIdentityReferenceReadStatus
{
    Complete,
    Incomplete,
    Collision,
}

internal sealed record TypedHorseIdentityPageEvidence(
    MigrationStoreSurface Surface,
    string Table,
    long RowCount,
    string BeforeHash,
    string ExpectedAfterHash,
    string? FirstKey = null,
    string? LastKey = null);

internal sealed record TypedHorseIdentityReferenceReadResult(
    TypedHorseIdentityReferenceReadStatus Status,
    IReadOnlyList<TypedHorseIdentityPageEvidence> Pages,
    long InspectedRows,
    long InspectedBytes,
    string? Failure)
{
    internal bool IsComplete => Status == TypedHorseIdentityReferenceReadStatus.Complete;
    internal bool HasCollision => Status == TypedHorseIdentityReferenceReadStatus.Collision;
}

internal sealed record TypedHorseIdentityPhysicalRow(long RowId, IReadOnlyList<object?> Values);
internal sealed record TypedHorseIdentityTransformedRow(
    long RowId,
    IReadOnlyList<object?> SourceValues,
    IReadOnlyList<object?> ExpectedValues);
internal sealed record TypedHorseIdentityExecutionPage(
    TypedHorseIdentityPageEvidence Evidence,
    string MembershipHash);

internal sealed class TypedHorseIdentityExecutionBudget
{
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _startedAt;
    private long _rows;
    private long _bytes;
    private long _summaryBytes;

    internal TypedHorseIdentityExecutionBudget(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _startedAt = _timeProvider.GetUtcNow();
    }

    internal void AddRow(long bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _rows++;
        _bytes += Math.Max(0, bytes);
        if (_rows > 1_000_000 || _bytes > 4L * 1024 * 1024 * 1024
            || _timeProvider.GetUtcNow() - _startedAt >= TimeSpan.FromMinutes(30))
            throw new InvalidDataException("Canonical physical evidence exceeded its inspection bound.");
    }

    internal void AddPreviouslyScanned(long rows, long bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (rows < 0 || bytes < 0) throw new InvalidDataException("Physical scan accounting is invalid.");
        _rows += rows;
        _bytes += bytes;
        EnsureWithinBounds(cancellationToken);
    }

    internal void EnsureWithinBounds(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_rows > 1_000_000 || _bytes > 4L * 1024 * 1024 * 1024
            || _timeProvider.GetUtcNow() - _startedAt >= TimeSpan.FromMinutes(30))
            throw new InvalidDataException("Canonical physical evidence exceeded its inspection bound.");
    }

    internal void AddPage(long bytes)
    {
        _summaryBytes += Math.Max(0, bytes);
        if (_summaryBytes > 1024 * 1024
            || _timeProvider.GetUtcNow() - _startedAt >= TimeSpan.FromMinutes(30))
            throw new InvalidDataException("Canonical physical evidence exceeded its summary bound.");
    }
}

/// <summary>
/// Reads the explicitly typed Horse-reference closure without retaining source rows or opening a writer.
/// Unknown affected payloads are rejected rather than interpreted by property-name substitution.
/// </summary>
internal sealed class TypedHorseIdentityReferenceReader
{
    private readonly IEventJsonSerializer? _eventJsonSerializer;
    private readonly IDomainEventFactory? _domainEventFactory;

    internal TypedHorseIdentityReferenceReader(
        IEventJsonSerializer? eventJsonSerializer = null,
        IDomainEventFactory? domainEventFactory = null)
    {
        _eventJsonSerializer = eventJsonSerializer;
        _domainEventFactory = domainEventFactory;
    }

    private const int PageSize = 128;
    private const int MaximumRows = 1_000_000;
    private const long MaximumInputBytes = 4L * 1024 * 1024 * 1024;
    private const int MaximumSerializedBytes = 64 * 1024;
    private const int MaximumJsonDepth = 16;
    private const int MaximumSummaryBytes = 1024 * 1024;
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions LocalQueueJsonOptions = CreateLocalQueueJsonOptions();

    private static readonly string[] TypedJsonNames =
    [
        "horseId", "previousHorseId", "winningHorseId", "sourceHorseId", "targetHorseId",
        "subjectId", "targetSubjectId", "subjectHorseId", "requestedByHorseId", "discoveredFromId",
        "entryId", "oldHorseId", "newHorseId",
    ];

    private static readonly string[] CollectionTables =
    [
        "collection_schema_history",
        "collection_resources", "collection_resource_suppressions", "collection_definitions",
        "collection_revisions", "collection_revision_impacts", "collection_states", "collection_requests",
        "collection_request_batch_bindings", "collection_tasks", "collection_active_tasks",
        "collection_attempts", "collection_attempt_stage_outcomes", "race_artifact_states",
        "race_scheduling_evidence", "resource_locations", "collection_task_outbox",
        "collection_execution_leases", "collection_dispatcher_fairness_state", "collection_platform_controls",
        "race_repair_holds", "collection_failure_notifications",
        "collection_subject_identification_selections", "collection_backfill_batches",
        "collection_batch_recovery_progress", "collection_batch_recovery_scan",
    ];

    internal static bool IsKnownDiagnosticPage(MigrationStoreSurface surface, string table) => surface switch
    {
        MigrationStoreSurface.EventStore => table is "Horses" or "HorseWeightHistories" or "HorseRaceHistories"
            or "JraSubjectProfileReadModel" or "Jockeys" or "Trainers" or "RacePredictionContexts"
            or "RaceResults" or "PredictionTickets" or "PredictionComparisons" or "MemoSubjects"
            or "JockeyRaceHistories" or "SnapshotEntity" or "HorseIdentityRepairCandidates"
            or "HorseIdentityRepairRedirects" or "SubjectIdentificationRepairIssues" or "EventEntity",
        MigrationStoreSurface.CollectionPlatform => CollectionTables.Contains(table, StringComparer.Ordinal),
        MigrationStoreSurface.PredictionSchedule => table == "prediction_candidates",
        MigrationStoreSurface.LocalCollectionQueue => table == "local_collection_messages",
        _ => false,
    };

    internal async Task<TypedHorseIdentityReferenceReadResult> InspectAsync(
        string eventStorePath,
        string collectionPath,
        string schedulePath,
        string? queuePath,
        IReadOnlyList<HorseIdentityMigrationMapping> mappings,
        CancellationToken cancellationToken,
        HorseIdentityMigrationInspectionProgress? inspectionProgress = null)
    {
        inspectionProgress?.StartPhase(HorseIdentityMigrationInspectionPhase.TypedReferences);
        var stopwatch = Stopwatch.StartNew();
        var pages = new List<TypedHorseIdentityPageEvidence>();
        var oldIds = mappings.Select(mapping => mapping.OldHorseId)
            .ToHashSet(StringComparer.Ordinal);
        var destinationIds = mappings.Select(mapping => mapping.DestinationHorseId)
            .ToHashSet(StringComparer.Ordinal);
        var replacements = mappings.ToDictionary(mapping => mapping.OldHorseId,
            mapping => mapping.DestinationHorseId, StringComparer.Ordinal);
        long inspectedRows = 0;
        long inputBytes = 0;
        var state = new ReadState(pages, oldIds, destinationIds, replacements,
            stopwatch, inspectedRows, inputBytes, inspectionProgress);

        try
        {
            await using var eventConnection = await TypedHorseIdentityStoreReaders.OpenReadOnlyAsync(
                eventStorePath, cancellationToken).ConfigureAwait(false);
            if (!await InspectEventStoreAsync(eventConnection, state, cancellationToken).ConfigureAwait(false))
                return state.Result(TypedHorseIdentityReferenceReadStatus.Incomplete);

            await using var collectionConnection = await TypedHorseIdentityStoreReaders.OpenReadOnlyAsync(
                collectionPath, cancellationToken).ConfigureAwait(false);
            if (!await InspectCollectionStoreAsync(collectionConnection, state, cancellationToken)
                    .ConfigureAwait(false))
                return state.Result(TypedHorseIdentityReferenceReadStatus.Incomplete);

            if (!await InspectAuxiliaryStoresAsync(schedulePath, queuePath, state, cancellationToken)
                    .ConfigureAwait(false))
                return state.Result(TypedHorseIdentityReferenceReadStatus.Incomplete);

            return state.Result(state.Collision
                ? TypedHorseIdentityReferenceReadStatus.Collision
                : TypedHorseIdentityReferenceReadStatus.Complete);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException)
        {
            return state.Result(TypedHorseIdentityReferenceReadStatus.Incomplete, "sqlite-read-failed");
        }
        catch (JsonException)
        {
            return state.Result(TypedHorseIdentityReferenceReadStatus.Incomplete, "invalid-json");
        }
    }

    private static async Task<bool> InspectAuxiliaryStoresAsync(
        string schedulePath, string? queuePath, ReadState state, CancellationToken cancellationToken)
    {
        if (File.Exists(schedulePath))
        {
            await using var schedule = await TypedHorseIdentityStoreReaders.OpenReadOnlyAsync(
                schedulePath, cancellationToken).ConfigureAwait(false);
            if (!await InspectSimpleTableAsync(schedule, MigrationStoreSurface.PredictionSchedule,
                    "prediction_candidates", "RaceId", ["Status", "FirstQueuedAt", "AvailableAt", "LeaseToken",
                    "LeaseExpiresAt", "LastError", "UpdatedAt"], state, cancellationToken)
                    .ConfigureAwait(false))
                return false;
        }

        if (queuePath is null || !File.Exists(queuePath)) return true;
        await using var queue = await TypedHorseIdentityStoreReaders.OpenReadOnlyAsync(
            queuePath, cancellationToken).ConfigureAwait(false);
        return await InspectQueueBodiesAsync(queue, state, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> InspectSimpleTableAsync(
        SqliteConnection connection, MigrationStoreSurface surface, string table, string keyColumn,
        IReadOnlyList<string> columns, ReadState state, CancellationToken cancellationToken)
    {
        long afterRowId = 0;
        while (true)
        {
            await using var command = connection.CreateCommand();
            var select = string.Join(",", new[] { keyColumn }.Concat(columns).Select(column =>
                $"CASE WHEN length(CAST(\"{column}\" AS BLOB)) <= $maxBytes THEN \"{column}\" ELSE NULL END, "
                + $"length(CAST(\"{column}\" AS BLOB))"));
            command.CommandText = $"SELECT rowid,{select} FROM \"{table}\" WHERE rowid > $after ORDER BY rowid LIMIT $limit;";
            command.CommandTimeout = 1;
            command.Parameters.AddWithValue("$maxBytes", MaximumSerializedBytes);
            command.Parameters.AddWithValue("$after", afterRowId);
            command.Parameters.AddWithValue("$limit", PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long rows = 0;
            long first = 0;
            long last = afterRowId;
            while (rows < PageSize && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var rowId = reader.GetInt64(0);
                if (first == 0) first = rowId;
                last = rowId;
                Append(hash, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                long rowBytes = sizeof(long);
                for (var index = 0; index < (reader.FieldCount - 1) / 2; index++)
                {
                    var valueIndex = 1 + index * 2;
                    var lengthIndex = valueIndex + 1;
                    var valueIsNull = reader.IsDBNull(valueIndex);
                    var lengthIsNull = reader.IsDBNull(lengthIndex);
                    if (valueIsNull != lengthIsNull) return false;
                    var value = valueIsNull ? "<null>" : StableSqlValue(reader.GetValue(valueIndex)) ?? "<null>";
                    var byteLength = valueIsNull ? 1 : reader.GetInt64(lengthIndex);
                    if (byteLength > MaximumSerializedBytes) return false;
                    Append(hash, value);
                    rowBytes += byteLength;
                }
                if (!state.AddRow(ref rows, ref state.InputBytes, rowBytes)) return false;
            }
            var hasMore = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (rows == 0) break;
            var digest = Convert.ToHexString(hash.GetHashAndReset());
            state.AddPage(surface, table, rows, digest, digest,
                first.ToString(System.Globalization.CultureInfo.InvariantCulture),
                last.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!hasMore) break;
            afterRowId = last;
        }
        return true;
    }

    private static async Task<bool> InspectQueueBodiesAsync(
        SqliteConnection connection, ReadState state, CancellationToken cancellationToken)
    {
        long afterRowId = 0;
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT rowid, message_id,
                       CASE WHEN length(CAST(body AS BLOB)) <= $maxBytes THEN body ELSE NULL END,
                       length(CAST(body AS BLOB)),
                       CASE WHEN length(CAST(visible_at AS BLOB)) <= $maxBytes THEN visible_at ELSE NULL END,
                       length(CAST(visible_at AS BLOB)),
                       CASE WHEN length(CAST(receipt_handle AS BLOB)) <= $maxBytes THEN receipt_handle ELSE NULL END,
                       length(CAST(receipt_handle AS BLOB)), receive_count, dead_letter,
                       CASE WHEN length(CAST(created_at AS BLOB)) <= $maxBytes THEN created_at ELSE NULL END,
                       length(CAST(created_at AS BLOB))
                FROM local_collection_messages WHERE rowid > $after ORDER BY rowid LIMIT $limit;
                """;
            command.CommandTimeout = 1;
            command.Parameters.AddWithValue("$maxBytes", MaximumSerializedBytes);
            command.Parameters.AddWithValue("$after", afterRowId);
            command.Parameters.AddWithValue("$limit", PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long rows = 0;
            long first = 0;
            long last = afterRowId;
            while (rows < PageSize && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3)
                    || reader.IsDBNull(4) || reader.IsDBNull(5) || reader.IsDBNull(7) != reader.IsDBNull(6)
                    || reader.IsDBNull(8) || reader.IsDBNull(9) || reader.IsDBNull(10) || reader.IsDBNull(11))
                    return false;
                var rowId = reader.GetInt64(0);
                if (first == 0) first = rowId;
                last = rowId;
                var messageId = reader.GetInt64(1);
                var body = reader.GetString(2);
                var bodyBytes = reader.GetInt64(3);
                var visibleAt = reader.GetString(4);
                var visibleAtBytes = reader.GetInt64(5);
                var receiptHandle = reader.IsDBNull(6) ? null : reader.GetString(6);
                var receiptHandleBytes = reader.IsDBNull(7) ? 0 : reader.GetInt64(7);
                var receiveCount = reader.GetInt32(8);
                var deadLetter = reader.GetInt32(9);
                var createdAt = reader.GetString(10);
                var createdAtBytes = reader.GetInt64(11);
                if (bodyBytes > MaximumSerializedBytes || visibleAtBytes > MaximumSerializedBytes
                    || receiptHandleBytes > MaximumSerializedBytes || createdAtBytes > MaximumSerializedBytes
                    || !ValidateJson(body)) return false;
                if (ContainsMappedTypedReference(body, state))
                    state.Failure ??= "queue-reference-requires-generation-recipe";
                else if (!IsSupportedLocalQueueBody(body))
                    state.Failure ??= "queue-body-shape-unsupported";
                var stableKey = rowId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Append(hash, stableKey);
                AppendLocalQueueRow(hash, messageId, body, visibleAt, receiptHandle,
                    receiveCount, deadLetter, createdAt);
                if (!state.AddRow(ref rows, ref state.InputBytes,
                        bodyBytes + visibleAtBytes + receiptHandleBytes + createdAtBytes
                        + Encoding.UTF8.GetByteCount(messageId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        + Encoding.UTF8.GetByteCount(receiveCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        + Encoding.UTF8.GetByteCount(deadLetter.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                    return false;
            }
            var hasMore = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (rows == 0) break;
            var digest = Convert.ToHexString(hash.GetHashAndReset());
            state.AddPage(MigrationStoreSurface.LocalCollectionQueue, "local_collection_messages", rows,
                digest, digest, first.ToString(System.Globalization.CultureInfo.InvariantCulture),
                last.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!hasMore) break;
            afterRowId = last;
        }
        return true;
    }

    private static JsonSerializerOptions CreateLocalQueueJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JstStoredDateTimeOffsetJsonConverter());
        return options;
    }

    private static bool IsSupportedLocalQueueBody(string body)
    {
        using var sourceDocument = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        if (sourceDocument.RootElement.ValueKind != JsonValueKind.Object) return false;
        var hasWakeId = sourceDocument.RootElement.TryGetProperty("wakeId", out _);
        var hasEnvelopeId = sourceDocument.RootElement.TryGetProperty("envelopeId", out _);
        if (hasWakeId == hasEnvelopeId) return false;

        var canonical = hasWakeId
            ? DeserializeSupportedWake(body)
            : DeserializeSupportedDispatchEnvelope(body);
        return canonical is not null && JsonNode.DeepEquals(JsonNode.Parse(body), JsonNode.Parse(canonical));
    }

    private static string? DeserializeSupportedWake(string body)
    {
        var wake = JsonSerializer.Deserialize<CollectionWakeSignal>(body, LocalQueueJsonOptions);
        if (wake is null || !new CollectionWakeSignalDto(wake.WakeId, wake.DispatchEnvelopeId,
                wake.ReservationToken, wake.ContractVersion).IsSupported()) return null;
        return JsonSerializer.Serialize(wake, LocalQueueJsonOptions);
    }

    private static string? DeserializeSupportedDispatchEnvelope(string body)
    {
        var envelope = JsonSerializer.Deserialize<CollectionDispatchEnvelope>(body, LocalQueueJsonOptions);
        return envelope is { } && envelope.IsSupported()
            ? JsonSerializer.Serialize(envelope, LocalQueueJsonOptions)
            : null;
    }

    private static void AppendLocalQueueRow(IncrementalHash hash, long messageId, string body,
        string visibleAt, string? receiptHandle, int receiveCount, int deadLetter, string createdAt)
    {
        foreach (var (column, value) in new[]
        {
            ("message_id", "integer:" + messageId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("body", "text:" + body),
            ("visible_at", "text:" + visibleAt),
            ("receipt_handle", receiptHandle is null ? "<null>" : "text:" + receiptHandle),
            ("receive_count", "integer:" + receiveCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("dead_letter", "integer:" + deadLetter.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("created_at", "text:" + createdAt),
        })
        {
            Append(hash, column);
            Append(hash, value);
        }
    }

    private async Task<bool> InspectEventStoreAsync(
        SqliteConnection connection, ReadState state, CancellationToken cancellationToken)
    {
        foreach (var (table, keyColumn) in new[]
        {
            ("Horses", "HorseId"), ("HorseWeightHistories", "HorseId"),
            ("HorseRaceHistories", "HorseId"), ("JraSubjectProfileReadModel", "SubjectId"),
        })
        {
            if (!await InspectKeyTableAsync(connection, state, table, keyColumn, cancellationToken)
                    .ConfigureAwait(false))
            {
                state.Failure ??= $"identity-key-scan-failed:{table}";
                return false;
            }
        }

        if (!await InspectMemoSubjectsAsync(connection, state, cancellationToken).ConfigureAwait(false))
        {
            state.Failure ??= "memo-key-scan-failed";
            return false;
        }
        if (!await InspectJsonReadModelsAsync(connection, state, cancellationToken).ConfigureAwait(false))
        {
            state.Failure ??= "read-model-json-scan-failed";
            return false;
        }
        if (!await InspectSnapshotsAsync(connection, state, cancellationToken).ConfigureAwait(false))
        {
            state.Failure ??= "snapshot-scan-failed";
            return false;
        }
        foreach (var (table, columns) in new[]
        {
            ("HorseIdentityRepairCandidates", new[] { "SourceHorseId", "TargetHorseId" }),
            ("HorseIdentityRepairRedirects", new[] { "SourceHorseId", "TargetHorseId" }),
            ("SubjectIdentificationRepairIssues", new[] { "SubjectId", "TargetSubjectId" }),
        })
        {
            if (!await InspectReferenceColumnsAsync(connection, state, table, columns, cancellationToken)
                    .ConfigureAwait(false))
            {
                state.Failure ??= $"reference-column-scan-failed:{table}";
                return false;
            }
        }

        return await InspectEventEntitiesAsync(connection, state, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> InspectJsonReadModelsAsync(
        SqliteConnection connection, ReadState state, CancellationToken cancellationToken)
    {
        foreach (var (table, keyColumn, jsonColumns) in new[]
        {
            ("Horses", "HorseId", new[] { "Aliases" }),
            ("JraSubjectProfileReadModel", "SubjectId", new[] { "Fields" }),
            ("Jockeys", "JockeyId", new[] { "Aliases" }),
            ("Trainers", "TrainerId", new[] { "Aliases" }),
            ("RacePredictionContexts", "RaceId", new[] { "Entries", "WeatherObservations", "TrackConditionObservations", "OddsSnapshots" }),
            ("RaceResults", "RaceId", new[] { "EntryResults", "EntryIndexes", "PayoutResult" }),
            ("PredictionTickets", "PredictionTicketId", new[] { "Marks", "Evaluations" }),
            ("HorseWeightHistories", "HorseId", new[] { "WeightHistory" }),
            ("PredictionComparisons", "RaceId", new[] { "TicketStates", "EntryIndexes", "EntryResults", "PayoutResult" }),
            ("MemoSubjects", "SubjectKey", new[] { "Memos" }),
            ("HorseRaceHistories", "HorseId", new[] { "Entries" }),
            ("JockeyRaceHistories", "JockeyId", new[] { "Entries" }),
        })
        {
            if (!await InspectJsonColumnsAsync(connection, state, table, keyColumn, jsonColumns,
                    cancellationToken).ConfigureAwait(false))
            {
                state.Failure ??= $"read-model-json-scan-failed:{table}";
                return false;
            }
        }
        return true;
    }

    private static async Task<bool> InspectJsonColumnsAsync(
        SqliteConnection connection, ReadState state, string table, string keyColumn,
        IReadOnlyList<string> jsonColumns, CancellationToken cancellationToken)
    {
        var tableColumns = new List<string>();
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = $"PRAGMA table_info(\"{table}\");";
            await using var schemaReader = await schema.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await schemaReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                tableColumns.Add(schemaReader.GetString(1));
        }
        if (tableColumns.Count == 0 || tableColumns.Any(column => column.Contains('"'))
            || !tableColumns.Contains(keyColumn, StringComparer.OrdinalIgnoreCase)
            || jsonColumns.Any(column => !tableColumns.Contains(column, StringComparer.OrdinalIgnoreCase)))
            return false;

        long afterRowId = 0;
        while (true)
        {
            var projection = string.Join(",", tableColumns.Select(column =>
                $"CASE WHEN length(CAST(\"{column}\" AS BLOB)) <= $maxBytes THEN \"{column}\" ELSE NULL END, "
                + $"length(CAST(\"{column}\" AS BLOB))"));
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT rowid,{projection} FROM \"{table}\" WHERE rowid > $after ORDER BY rowid LIMIT $limit;";
            command.CommandTimeout = 1;
            command.Parameters.AddWithValue("$maxBytes", MaximumSerializedBytes);
            command.Parameters.AddWithValue("$after", afterRowId);
            command.Parameters.AddWithValue("$limit", PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var expectedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long rows = 0;
            long first = 0;
            long last = afterRowId;
            while (rows < PageSize && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0)) return false;
                var rowId = reader.GetInt64(0);
                if (first == 0) first = rowId;
                last = rowId;
                var sourceValues = new string?[tableColumns.Count];
                var expectedValues = new string?[tableColumns.Count];
                var rawValues = new string?[tableColumns.Count];
                long rowBytes = sizeof(long);
                for (var index = 0; index < tableColumns.Count; index++)
                {
                    var valueIndex = 1 + index * 2;
                    var lengthIndex = valueIndex + 1;
                    var valueIsNull = reader.IsDBNull(valueIndex);
                    var lengthIsNull = reader.IsDBNull(lengthIndex);
                    if (valueIsNull != lengthIsNull) return false;
                    if (!valueIsNull)
                    {
                        var byteLength = reader.GetInt64(lengthIndex);
                        if (byteLength > MaximumSerializedBytes) return false;
                        var value = reader.GetValue(valueIndex);
                        rawValues[index] = value is string text ? text : null;
                        sourceValues[index] = StableSqlValue(value);
                        rowBytes += byteLength;
                    }
                    expectedValues[index] = sourceValues[index];
                }
                var keyIndex = tableColumns.FindIndex(column => column.Equals(keyColumn,
                    StringComparison.OrdinalIgnoreCase));
                if (rawValues[keyIndex] is not { } key) return false;
                var keyChanges = table is "Horses" or "HorseWeightHistories" or "HorseRaceHistories"
                    or "JraSubjectProfileReadModel";
                if (keyChanges && state.Replacements.TryGetValue(key, out var mappedKey))
                    expectedValues[keyIndex] = "text:" + mappedKey;
                var raceIndex = tableColumns.FindIndex(column => column.Equals("RaceId",
                    StringComparison.OrdinalIgnoreCase));
                var raceId = raceIndex >= 0 ? rawValues[raceIndex] : key;
                for (var index = 0; index < tableColumns.Count; index++)
                {
                    var column = tableColumns[index];
                    var jsonColumnIndex = Array.FindIndex(jsonColumns.ToArray(), candidate =>
                        candidate.Equals(column, StringComparison.OrdinalIgnoreCase));
                    if (jsonColumnIndex < 0 || rawValues[index] is not { } json) continue;
                    if (!ValidateJson(json)) return false;
                    if (ContainsNullHorseDerivedEntry(json, state, raceId))
                        state.Failure ??= "null-horse-derived-entry";
                    if (!TryTransformKnownJson(table, jsonColumns[jsonColumnIndex], json, key, raceId, state,
                            out var expectedJson))
                    {
                        state.Failure ??= ContainsMappedTypedReference(json, state)
                            ? ContainsNullHorseDerivedEntry(json, state, raceId)
                                ? "null-horse-derived-entry"
                                : "read-model-json-transform-recipe-required"
                                : "read-model-json-shape-unsupported";
                    }
                    expectedValues[index] = "text:" + expectedJson;
                }

                Append(hash, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(expectedHash, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                for (var index = 0; index < tableColumns.Count; index++)
                {
                    Append(hash, tableColumns[index]);
                    Append(expectedHash, tableColumns[index]);
                    Append(hash, sourceValues[index] ?? "<null>");
                    Append(expectedHash, expectedValues[index] ?? "<null>");
                }
                if (!state.AddRow(ref rows, ref state.InputBytes, rowBytes)) return false;
            }
            var hasMore = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (rows == 0)
            {
                if (afterRowId == 0)
                {
                    var emptyDigest = Convert.ToHexString(SHA256.HashData([]));
                    state.AddPage(MigrationStoreSurface.EventStore, table, 0, emptyDigest, emptyDigest);
                }
                break;
            }
            state.AddPage(MigrationStoreSurface.EventStore, table, rows,
                Convert.ToHexString(hash.GetHashAndReset()), Convert.ToHexString(expectedHash.GetHashAndReset()),
                first.ToString(System.Globalization.CultureInfo.InvariantCulture),
                last.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!hasMore) break;
            afterRowId = last;
        }
        return true;
    }

    private static bool TryTransformKnownJson(
        string table, string column, string json, string rowKey, string? raceId,
        ReadState state, out string expectedJson)
    {
        expectedJson = json;
        var options = CreateJsonOptions();
        try
        {
            switch ((table, column))
            {
                case ("RacePredictionContexts", "Entries"):
                    {
                        var value = JsonSerializer.Deserialize<List<RacePredictionContextEntry>>(json, options);
                        if (value is null) return false;
                        return raceId is not null && SerializeExact(json,
                            value.Select(entry => TransformEntry(entry, raceId, state)).ToList(), options,
                            out expectedJson);
                    }
                case ("RaceResults", "EntryResults"):
                case ("PredictionComparisons", "EntryResults"):
                    {
                        var value = JsonSerializer.Deserialize<List<EntryResultSnapshot>>(json, options);
                        if (value is null) return false;
                        return raceId is not null && SerializeExact(json,
                            value.Select(entry => TransformEntry(entry, raceId, state)).ToList(), options,
                            out expectedJson);
                    }
                case ("RaceResults", "EntryIndexes"):
                case ("PredictionComparisons", "EntryIndexes"):
                    {
                        var value = JsonSerializer.Deserialize<List<RaceEntryIndexSnapshot>>(json, options);
                        if (value is null) return false;
                        return raceId is not null && SerializeExact(json,
                            value.Select(entry => TransformEntry(entry, raceId, state)).ToList(), options,
                            out expectedJson);
                    }
                case ("JockeyRaceHistories", "Entries"):
                    {
                        var value = JsonSerializer.Deserialize<List<JockeyRaceHistoryEntry>>(json, options);
                        if (value is null) return false;
                        return SerializeExact(json, value.Select(entry =>
                        {
                            var horseId = MapOrKeep(entry.HorseId, state)!;
                            return entry with
                            {
                                HorseId = horseId,
                                EntryId = TransformEntryId(entry.EntryId, entry.RaceId, entry.HorseId, horseId),
                            };
                        }).ToList(), options, out expectedJson);
                    }
                case ("HorseRaceHistories", "Entries"):
                    {
                        var value = JsonSerializer.Deserialize<List<HorseRaceHistoryEntry>>(json, options);
                        if (value is null) return false;
                        var horseId = state.Replacements.GetValueOrDefault(rowKey, rowKey);
                        return SerializeExact(json, value.Select(entry => entry with
                        {
                            EntryId = TransformEntryId(entry.EntryId, entry.RaceId, rowKey, horseId),
                        }).ToList(), options, out expectedJson);
                    }
                case ("RacePredictionContexts", "WeatherObservations"):
                    return SerializeExact(json,
                        JsonSerializer.Deserialize<List<WeatherObservationSnapshot>>(json, options), options,
                        out expectedJson);
                case ("RacePredictionContexts", "TrackConditionObservations"):
                    return SerializeExact(json,
                        JsonSerializer.Deserialize<List<TrackConditionSnapshot>>(json, options), options,
                        out expectedJson);
                case ("RacePredictionContexts", "OddsSnapshots"):
                    {
                        var value = JsonSerializer.Deserialize<List<RaceOddsSnapshot>>(json, options);
                        if (value is null || raceId is null) return false;
                        return SerializeExact(json, value.Select(snapshot => snapshot with
                        {
                            Assignments = snapshot.Assignments?.Select(assignment =>
                            {
                                var horseId = MapOrKeep(assignment.HorseId, state)!;
                                return assignment with
                                {
                                    HorseId = horseId,
                                    EntryId = TransformEntryId(assignment.EntryId, raceId,
                                        assignment.HorseId, horseId),
                                };
                            }).ToList(),
                        }).ToList(), options, out expectedJson);
                    }
                case ("RaceResults", "PayoutResult"):
                case ("PredictionComparisons", "PayoutResult"):
                    return json.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)
                        ? JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse("null"))
                        : SerializeExact(json,
                            JsonSerializer.Deserialize<PayoutResultSnapshot>(json, options), options,
                            out expectedJson);
                case ("MemoSubjects", "Memos"):
                    {
                        var value = JsonSerializer.Deserialize<List<MemoSnapshot>>(json, options);
                        if (value is null) return false;
                        return SerializeExact(json, value.Select(memo => memo with
                        {
                            Subjects = memo.Subjects.Select(subject =>
                                subject.SubjectType.Equals("Horse", StringComparison.OrdinalIgnoreCase)
                                    ? subject with { SubjectId = MapOrKeep(subject.SubjectId, state)! }
                                    : subject).ToList(),
                        }).ToList(), options, out expectedJson);
                    }
                case ("Horses", "Aliases"):
                    return SerializeExact(json, JsonSerializer.Deserialize<List<HorseAliasEntry>>(json, options),
                        options, out expectedJson);
                case ("Jockeys", "Aliases"):
                    return SerializeExact(json, JsonSerializer.Deserialize<List<JockeyAliasEntry>>(json, options),
                        options, out expectedJson);
                case ("Trainers", "Aliases"):
                    return SerializeExact(json, JsonSerializer.Deserialize<List<TrainerAliasEntry>>(json, options),
                        options, out expectedJson);
                case ("HorseWeightHistories", "WeightHistory"):
                    {
                        var value = JsonSerializer.Deserialize<List<HorseWeightEntry>>(json, options);
                        if (value is null) return false;
                        var horseId = state.Replacements.GetValueOrDefault(rowKey, rowKey);
                        return SerializeExact(json, value.Select(entry => entry with
                        {
                            EntryId = TransformEntryId(entry.EntryId, entry.RaceId, rowKey, horseId),
                        }).ToList(), options, out expectedJson);
                    }
                case ("PredictionTickets", "Marks"):
                    {
                        var value = JsonSerializer.Deserialize<List<PredictionMarkSnapshot>>(json, options);
                        return value is not null && raceId is not null && SerializeExact(json,
                            value.Select(mark => mark with
                            {
                                EntryId = TransformTicketEntryId(mark.EntryId, raceId, state),
                            }).ToList(), options, out expectedJson);
                    }
                case ("PredictionTickets", "Evaluations"):
                    return SerializeExact(json,
                        JsonSerializer.Deserialize<List<PredictionEvaluationSnapshot>>(json, options), options,
                        out expectedJson);
                case ("PredictionComparisons", "TicketStates"):
                    {
                        var value = JsonSerializer.Deserialize<List<PredictionComparisonTicketState>>(json, options);
                        if (value is null || raceId is null) return false;
                        foreach (var ticket in value)
                            ticket.Marks = ticket.Marks.Select(mark => mark with
                            {
                                EntryId = TransformTicketEntryId(mark.EntryId, raceId, state),
                            }).ToList();
                        return SerializeExact(json, value, options, out expectedJson);
                    }
                case ("JraSubjectProfileReadModel", "Fields"):
                    return SerializeExact(json,
                        JsonSerializer.Deserialize<Dictionary<string, string>>(json, options), options,
                        out expectedJson);
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Execution uses the same finite DTO/property recipes as the read-only evidence pass.
    // Keep this entry explicit: callers cannot request arbitrary JSON property replacement.
    internal static bool TryTransformKnownJsonForExecution(
        string table, string column, string json, string rowKey, string? raceId,
        IReadOnlyList<HorseIdentityMigrationMapping> mappings, out string expectedJson)
    {
        var oldIds = mappings.Select(mapping => mapping.OldHorseId).ToHashSet(StringComparer.Ordinal);
        var destinationIds = mappings.Select(mapping => mapping.DestinationHorseId).ToHashSet(StringComparer.Ordinal);
        var replacements = mappings.ToDictionary(mapping => mapping.OldHorseId,
            mapping => mapping.DestinationHorseId, StringComparer.Ordinal);
        var state = new ReadState([], oldIds, destinationIds, replacements,
            Stopwatch.StartNew(), 0, 0, null);
        return TryTransformKnownJson(table, column, json, rowKey, raceId, state, out expectedJson);
    }

    internal static string TransformExactHorseReferenceForExecution(
        string value, IReadOnlyList<HorseIdentityMigrationMapping> mappings) =>
        mappings.FirstOrDefault(mapping => string.Equals(mapping.OldHorseId, value, StringComparison.Ordinal))
            ?.DestinationHorseId ?? value;

    internal static bool TryTransformCollectionMetadataForExecution(
        string json, IReadOnlyList<HorseIdentityMigrationMapping> mappings, out string expectedJson)
    {
        var oldIds = mappings.Select(mapping => mapping.OldHorseId).ToHashSet(StringComparer.Ordinal);
        var destinationIds = mappings.Select(mapping => mapping.DestinationHorseId).ToHashSet(StringComparer.Ordinal);
        var replacements = mappings.ToDictionary(mapping => mapping.OldHorseId,
            mapping => mapping.DestinationHorseId, StringComparer.Ordinal);
        var state = new ReadState([], oldIds, destinationIds, replacements,
            Stopwatch.StartNew(), 0, 0, null);
        return TryTransformCollectionMetadata(json, state, out expectedJson);
    }

    internal bool TryTransformEventForExecution(
        TypedHorseIdentityEventRecord record, IReadOnlyList<HorseIdentityMigrationMapping> mappings,
        TypedHorseIdentityTicketStreamContext? ticketContext, out TypedHorseIdentityEventRecord expectedRecord)
    {
        expectedRecord = record;
        if (_eventJsonSerializer is null || _domainEventFactory is null) return false;
        var replacements = mappings.ToDictionary(mapping => mapping.OldHorseId,
            mapping => mapping.DestinationHorseId, StringComparer.Ordinal);
        var transformation = new TypedHorseIdentityEventTransformer(_eventJsonSerializer,
            _domainEventFactory, replacements).Transform(record, ticketContext);
        if (transformation.IsAccepted && transformation.Event is not null)
        {
            expectedRecord = transformation.Event;
            return true;
        }
        return IsKnownUnchangedSubjectEvent(record);
    }

    private static bool SerializeExact<T>(string source, T? value, JsonSerializerOptions options,
        out string expectedJson)
    {
        expectedJson = source;
        if (value is null) return false;
        var serialized = JsonSerializer.Serialize(value, options);
        var sourceNode = JsonNode.Parse(source);
        var expectedNode = JsonNode.Parse(serialized);
        if (!SameJsonShape(sourceNode, expectedNode)) return false;
        CopyTypedIdentityValues(sourceNode, expectedNode);
        expectedJson = sourceNode?.ToJsonString() ?? serialized;
        return true;
    }

    private static void CopyTypedIdentityValues(JsonNode? source, JsonNode? expected)
    {
        if (source is JsonObject sourceObject && expected is JsonObject expectedObject)
        {
            foreach (var propertyName in sourceObject.Select(pair => pair.Key).ToArray())
            {
                if (!expectedObject.TryGetPropertyValue(propertyName, out var value)) continue;
                if (TypedJsonNames.Contains(propertyName, StringComparer.OrdinalIgnoreCase))
                    sourceObject[propertyName] = value?.DeepClone();
                else if (sourceObject[propertyName] is { } sourceValue)
                    CopyTypedIdentityValues(sourceValue, value);
            }
        }
        if (source is JsonArray sourceArray && expected is JsonArray expectedArray)
            for (var index = 0; index < sourceArray.Count; index++)
                CopyTypedIdentityValues(sourceArray[index], expectedArray[index]);
    }

    private static bool SameJsonShape(JsonNode? source, JsonNode? expected)
    {
        if (source is null || expected is null) return source is null && expected is null;
        if (source is JsonObject sourceObject && expected is JsonObject expectedObject)
        {
            if (sourceObject.Count != expectedObject.Count) return false;
            foreach (var (name, sourceValue) in sourceObject)
                if (!expectedObject.TryGetPropertyValue(name, out var expectedValue)
                    || !SameJsonShape(sourceValue, expectedValue)) return false;
            return true;
        }
        if (source is JsonArray sourceArray && expected is JsonArray expectedArray)
            return sourceArray.Count == expectedArray.Count
                && sourceArray.Zip(expectedArray).All(pair => SameJsonShape(pair.First, pair.Second));
        using var sourceDocument = JsonDocument.Parse(source.ToJsonString());
        using var expectedDocument = JsonDocument.Parse(expected.ToJsonString());
        return sourceDocument.RootElement.ValueKind == expectedDocument.RootElement.ValueKind;
    }

    private static RacePredictionContextEntry TransformEntry(
        RacePredictionContextEntry entry, string raceId, ReadState state)
    {
        var horseId = MapOrKeep(entry.HorseId, state)!;
        return entry with
        {
            HorseId = horseId,
            EntryId = TransformEntryId(entry.EntryId, raceId, entry.HorseId, horseId),
        };
    }

    private static EntryResultSnapshot TransformEntry(
        EntryResultSnapshot entry, string raceId, ReadState state)
    {
        var horseId = MapOrKeep(entry.HorseId, state)!;
        return entry with
        {
            HorseId = horseId,
            EntryId = TransformEntryId(entry.EntryId, raceId, entry.HorseId, horseId),
        };
    }

    private static RaceEntryIndexSnapshot TransformEntry(
        RaceEntryIndexSnapshot entry, string raceId, ReadState state)
    {
        var horseId = MapOrKeep(entry.HorseId, state)!;
        return entry with
        {
            HorseId = horseId,
            EntryId = TransformEntryId(entry.EntryId, raceId, entry.HorseId, horseId),
        };
    }

    private static string TransformEntryId(string entryId, string? raceId, string oldHorseId, string newHorseId) =>
        raceId is not null && !string.Equals(oldHorseId, newHorseId, StringComparison.Ordinal)
            && string.Equals(entryId, HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator
                .BuildRaceEntryId(raceId, oldHorseId), StringComparison.Ordinal)
            ? HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator.BuildRaceEntryId(raceId, newHorseId)
            : entryId;

    private static string TransformTicketEntryId(string entryId, string raceId, ReadState state)
    {
        foreach (var (oldHorseId, newHorseId) in state.Replacements)
            if (string.Equals(entryId, HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator
                    .BuildRaceEntryId(raceId, oldHorseId), StringComparison.Ordinal))
                return HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator.BuildRaceEntryId(
                    raceId, newHorseId);
        return entryId;
    }

    private static string? MapOrKeep(string? value, ReadState state) =>
        value is null ? null : state.Replacements.GetValueOrDefault(value, value);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JstStoredDateTimeOffsetJsonConverter());
        return options;
    }

    private static bool ContainsNullHorseDerivedEntry(string json, ReadState state, string? raceId)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        return ContainsNullHorseDerivedEntry(document.RootElement, state, raceId);
    }

    private static bool ContainsNullHorseDerivedEntry(JsonElement element, ReadState state, string? raceId)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            string? entryId = null;
            var entryRaceId = raceId;
            var horseIsNull = false;
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("raceId", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                    entryRaceId = property.Value.GetString();
                if (property.Name.Equals("entryId", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                    entryId = property.Value.GetString();
                if (property.Name.Equals("horseId", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Null)
                    horseIsNull = true;
                if (ContainsNullHorseDerivedEntry(property.Value, state, entryRaceId)) return true;
            }
            if (horseIsNull && entryId is not null && entryRaceId is not null
                && state.OldIds.Any(oldId => string.Equals(entryId,
                    HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator
                        .BuildRaceEntryId(entryRaceId, oldId), StringComparison.Ordinal)))
                return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                if (ContainsNullHorseDerivedEntry(child, state, raceId)) return true;
        }
        return false;
    }

    private static async Task<bool> InspectSnapshotsAsync(
        SqliteConnection connection, ReadState state, CancellationToken cancellationToken)
    {
        long afterRowId = 0;
        var affectedProjectionAggregateNames = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(RaceAggregate), nameof(MemoAggregate), nameof(PredictionTicketAggregate),
        };
        var sourceHorseSnapshotExists = false;
        var destinationHorseSnapshotExists = false;
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT rowid,
                       CASE WHEN length(CAST(AggregateId AS BLOB)) <= $maxBytes THEN AggregateId ELSE NULL END,
                       length(CAST(AggregateId AS BLOB)),
                       CASE WHEN length(CAST(AggregateName AS BLOB)) <= $maxBytes THEN AggregateName ELSE NULL END,
                       length(CAST(AggregateName AS BLOB)), AggregateSequenceNumber,
                       CASE WHEN length(CAST(Data AS BLOB)) <= $maxBytes THEN Data ELSE NULL END,
                       length(CAST(Data AS BLOB)),
                       CASE WHEN length(CAST(Metadata AS BLOB)) <= $maxBytes THEN Metadata ELSE NULL END,
                       length(CAST(Metadata AS BLOB))
                FROM SnapshotEntity WHERE rowid > $after ORDER BY rowid LIMIT $limit;
                """;
            command.CommandTimeout = 1;
            command.Parameters.AddWithValue("$maxBytes", MaximumSerializedBytes);
            command.Parameters.AddWithValue("$after", afterRowId);
            command.Parameters.AddWithValue("$limit", PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long rows = 0;
            long first = 0;
            long last = afterRowId;
            while (rows < PageSize && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (Enumerable.Range(0, reader.FieldCount).Any(reader.IsDBNull)) return false;
                var rowId = reader.GetInt64(0);
                if (first == 0) first = rowId;
                last = rowId;
                var aggregateId = reader.GetString(1);
                var aggregateIdBytes = reader.GetInt64(2);
                var aggregateName = reader.GetString(3);
                var aggregateNameBytes = reader.GetInt64(4);
                var sequence = reader.GetInt32(5);
                var data = reader.GetString(6);
                var dataBytes = reader.GetInt64(7);
                var metadata = reader.GetString(8);
                var metadataBytes = reader.GetInt64(9);
                if (aggregateIdBytes > MaximumSerializedBytes || aggregateNameBytes > MaximumSerializedBytes
                    || dataBytes > MaximumSerializedBytes || metadataBytes > MaximumSerializedBytes
                    || !ValidateJson(data) || !ValidateJson(metadata)) return false;
                var isHorseSnapshot = string.Equals(aggregateName, nameof(HorseAggregate), StringComparison.Ordinal);
                if (isHorseSnapshot && state.OldIds.Contains(aggregateId)) sourceHorseSnapshotExists = true;
                if (isHorseSnapshot && state.DestinationIds.Contains(aggregateId)) destinationHorseSnapshotExists = true;
                if ((isHorseSnapshot && state.OldIds.Contains(aggregateId))
                    || ContainsMappedTypedReference(data, state) || ContainsMappedTypedReference(metadata, state)
                    || affectedProjectionAggregateNames.Contains(aggregateName))
                    state.Failure ??= "snapshot-recipe-required";
                Append(hash, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(hash, aggregateId);
                Append(hash, aggregateName);
                Append(hash, sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(hash, data);
                Append(hash, metadata);
                if (!state.AddRow(ref rows, ref state.InputBytes,
                        aggregateIdBytes + aggregateNameBytes + dataBytes + metadataBytes)) return false;
            }
            var hasMore = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (rows == 0)
            {
                if (afterRowId == 0)
                {
                    var emptyDigest = Convert.ToHexString(SHA256.HashData([]));
                    state.AddPage(MigrationStoreSurface.EventStore, "SnapshotEntity", 0, emptyDigest, emptyDigest);
                }
                break;
            }
            var digest = Convert.ToHexString(hash.GetHashAndReset());
            state.AddPage(MigrationStoreSurface.EventStore, "SnapshotEntity", rows, digest, digest,
                first.ToString(System.Globalization.CultureInfo.InvariantCulture),
                last.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!hasMore) break;
            afterRowId = last;
        }
        state.Collision |= sourceHorseSnapshotExists && destinationHorseSnapshotExists;
        return true;
    }

    private static async Task<bool> InspectCollectionStoreAsync(
        SqliteConnection connection, ReadState state, CancellationToken cancellationToken)
    {
        var actualTables = new HashSet<string>(StringComparer.Ordinal);
        await using (var inventory = connection.CreateCommand())
        {
            inventory.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            await using var tables = await inventory.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await tables.ReadAsync(cancellationToken).ConfigureAwait(false))
                actualTables.Add(tables.GetString(0));
        }
        if (!actualTables.SetEquals(CollectionTables))
        {
            state.Failure ??= "collection-table-inventory-unsupported";
            return false;
        }

        var naturalIds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["collection_resources"] = new(StringComparer.Ordinal),
            ["collection_resource_suppressions"] = new(StringComparer.Ordinal),
        };
        foreach (var table in CollectionTables)
        {
            var columns = new List<string>();
            await using (var schema = connection.CreateCommand())
            {
                schema.CommandText = $"PRAGMA table_info(\"{table}\");";
                await using var schemaReader = await schema.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await schemaReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    columns.Add(schemaReader.GetString(1));
            }
            if (columns.Count == 0 || columns.Any(column => column.Contains('"')))
            {
                state.Failure ??= "collection-table-schema-unsupported";
                return false;
            }

            long afterRowId = 0;
            while (true)
            {
                await using var command = connection.CreateCommand();
                var projection = string.Join(",", columns.Select(column =>
                    $"CASE WHEN length(CAST(\"{column}\" AS BLOB)) <= $maxBytes THEN \"{column}\" ELSE NULL END, "
                    + $"length(CAST(\"{column}\" AS BLOB))"));
                command.CommandText = $"SELECT rowid,{projection} FROM \"{table}\" WHERE rowid > $after ORDER BY rowid LIMIT $limit;";
                command.CommandTimeout = 1;
                command.Parameters.AddWithValue("$maxBytes", MaximumSerializedBytes);
                command.Parameters.AddWithValue("$after", afterRowId);
                command.Parameters.AddWithValue("$limit", PageSize + 1);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                using var before = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long pageRows = 0;
                long lastRowId = afterRowId;
                long firstRowId = 0;
                var hasMore = false;
                while (pageRows < PageSize && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var rowId = reader.GetInt64(0);
                    if (firstRowId == 0) firstRowId = rowId;
                    lastRowId = rowId;
                    var source = new string?[columns.Count];
                    var target = new string?[columns.Count];
                    var rawValues = new object?[columns.Count];
                    long rowBytes = sizeof(long);
                    for (var index = 0; index < columns.Count; index++)
                    {
                        var valueIndex = 1 + index * 2;
                        var lengthIndex = valueIndex + 1;
                        var valueIsNull = reader.IsDBNull(valueIndex);
                        var lengthIsNull = reader.IsDBNull(lengthIndex);
                        if (valueIsNull != lengthIsNull) return false;
                        if (!valueIsNull)
                        {
                            var byteLength = reader.GetInt64(lengthIndex);
                            if (byteLength > MaximumSerializedBytes) return false;
                            rawValues[index] = reader.GetValue(valueIndex);
                            source[index] = StableSqlValue(rawValues[index]!);
                            rowBytes += byteLength;
                        }
                        target[index] = source[index];
                    }
                    var values = columns.Select((column, index) => (column, index))
                        .ToDictionary(pair => pair.column, pair => pair.index, StringComparer.OrdinalIgnoreCase);
                    string? Get(string name) => values.TryGetValue(name, out var index)
                        ? rawValues[index] is string rawText ? rawText : null
                        : null;
                    void Set(string name, string? replacement)
                    {
                        if (values.TryGetValue(name, out var index))
                        {
                            rawValues[index] = replacement;
                            target[index] = replacement is null ? null : "text:" + replacement;
                        }
                    }
                    var type = Get("Type") ?? Get("ResourceType") ?? Get("CanonicalResourceType");
                    var provider = Get("Provider") ?? Get("CanonicalResourceProvider");
                    var isHorseResource = string.Equals(type, "Horse", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(provider, "JRA", StringComparison.OrdinalIgnoreCase);
                    var isNaturalKey = table is "collection_resources" or "collection_resource_suppressions";
                    if (isNaturalKey && isHorseResource && Get("ResourceId") is { } resourceId)
                    {
                        if (state.OldIds.Contains(resourceId) || state.DestinationIds.Contains(resourceId))
                            naturalIds[table].Add(resourceId);
                        Set("ResourceId", state.Replacements.GetValueOrDefault(resourceId, resourceId));
                    }
                    if (table == "collection_failure_notifications" && isHorseResource
                        && Get("CanonicalResourceId") is { } canonicalId)
                        Set("CanonicalResourceId", state.Replacements.GetValueOrDefault(canonicalId, canonicalId));
                    if (table == "collection_subject_identification_selections" && isHorseResource
                        && Get("ResourceId") is { } selectedId)
                        Set("ResourceId", state.Replacements.GetValueOrDefault(selectedId, selectedId));
                    if (Get("SubjectHorseId") is { } subjectHorseId)
                        Set("SubjectHorseId", state.Replacements.GetValueOrDefault(subjectHorseId, subjectHorseId));
                    if (Get("RequestedByHorseId") is { } requestedByHorseId)
                        Set("RequestedByHorseId", state.Replacements.GetValueOrDefault(requestedByHorseId, requestedByHorseId));
                    foreach (var jsonColumn in new[] { "AttributesJson", "MetadataJson" })
                    {
                        if (Get(jsonColumn) is not { } json) continue;
                        if (!TryTransformCollectionMetadata(json, state, out var expectedJson))
                        {
                            if (ContainsMappedTypedReference(json, state))
                                state.Failure ??= "collection-metadata-transform-recipe-required";
                            else
                                state.Failure ??= !ValidateJson(json) ? "collection-metadata-json-invalid"
                                    : "collection-metadata-shape-unsupported";
                        }
                        else Set(jsonColumn, expectedJson);
                    }

                    Append(before, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Append(expected, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    for (var index = 0; index < columns.Count; index++)
                    {
                        Append(before, columns[index]);
                        Append(expected, columns[index]);
                        Append(before, source[index] ?? "<null>");
                        Append(expected, target[index] ?? "<null>");
                    }
                    if (!state.AddRow(ref pageRows, ref state.InputBytes, rowBytes)) return false;
                }
                hasMore = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (pageRows == 0)
                {
                    if (afterRowId == 0)
                    {
                        var emptyDigest = Convert.ToHexString(SHA256.HashData([]));
                        state.AddPage(MigrationStoreSurface.CollectionPlatform, table, 0, emptyDigest, emptyDigest);
                    }
                    break;
                }
                state.AddPage(MigrationStoreSurface.CollectionPlatform, table, pageRows,
                    Convert.ToHexString(before.GetHashAndReset()), Convert.ToHexString(expected.GetHashAndReset()),
                    firstRowId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    lastRowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (!hasMore) break;
                afterRowId = lastRowId;
            }
        }
        foreach (var ids in naturalIds.Values)
            if (state.OldIds.Any(ids.Contains) && state.DestinationIds.Any(ids.Contains))
            {
                state.Collision = true;
                state.Failure ??= "resource-natural-key-collision";
            }
        // Selection and failure rows are historical evidence, not natural-key uniqueness.
        return true;
    }

    private static string? StableSqlValue(object value) => value switch
    {
        DBNull => null,
        string text => "text:" + text,
        byte[] bytes => "blob:" + Convert.ToBase64String(bytes),
        long number => "integer:" + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        int number => "integer:" + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        double number => "real:" + number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        _ => value.GetType().FullName + ":" + Convert.ToString(value,
            System.Globalization.CultureInfo.InvariantCulture),
    };

    internal static string ComputeMembershipHash(IReadOnlyList<long> rowIds)
    {
        ArgumentNullException.ThrowIfNull(rowIds);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "rowid-membership-v1");
        foreach (var rowId in rowIds) Append(hash, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static (string BeforeHash, string ExpectedAfterHash) ComputePhysicalPageHashes(
        IReadOnlyList<string> orderedColumns,
        IReadOnlyList<TypedHorseIdentityTransformedRow> rows)
    {
        using var before = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var after = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rows)
        {
            Append(before, row.RowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(after, row.RowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (var index = 0; index < orderedColumns.Count; index++)
            {
                Append(before, orderedColumns[index]);
                Append(after, orderedColumns[index]);
                Append(before, StableSqlValue(row.SourceValues[index] ?? DBNull.Value) ?? "<null>");
                Append(after, StableSqlValue(row.ExpectedValues[index] ?? DBNull.Value) ?? "<null>");
            }
        }
        return (Convert.ToHexString(before.GetHashAndReset()), Convert.ToHexString(after.GetHashAndReset()));
    }

    internal static bool TryTransformPhysicalRow(
        MigrationStoreSurface surface,
        string table,
        IReadOnlyList<string> orderedColumns,
        IReadOnlyList<object?> sourceValues,
        IReadOnlyList<HorseIdentityMigrationMapping> mappings,
        out object?[] expectedValues)
    {
        var values = sourceValues.ToArray();
        expectedValues = values;
        if (orderedColumns.Count != sourceValues.Count || orderedColumns.Count == 0) return false;
        var indexes = orderedColumns.Select((column, index) => (column, index))
            .ToDictionary(item => item.column, item => item.index, StringComparer.OrdinalIgnoreCase);
        object? Get(string column) => indexes.TryGetValue(column, out var index) ? values[index] : null;
        bool SetText(string column, string? value)
        {
            if (!indexes.TryGetValue(column, out var index)) return false;
            values[index] = value;
            return true;
        }
        string? Text(string column) => Get(column) as string;
        string Map(string value) => TransformExactHorseReferenceForExecution(value, mappings);

        if (surface == MigrationStoreSurface.CollectionPlatform)
        {
            var type = Text("Type") ?? Text("ResourceType") ?? Text("CanonicalResourceType");
            var provider = Text("Provider") ?? Text("CanonicalResourceProvider");
            var isHorseResource = string.Equals(type, "Horse", StringComparison.OrdinalIgnoreCase)
                && string.Equals(provider, "JRA", StringComparison.OrdinalIgnoreCase);
            if (table is "collection_resources" or "collection_resource_suppressions"
                && isHorseResource && Text("ResourceId") is { } resourceId
                && !SetText("ResourceId", Map(resourceId))) return false;
            if (table == "collection_failure_notifications" && isHorseResource
                && Text("CanonicalResourceId") is { } canonicalId
                && !SetText("CanonicalResourceId", Map(canonicalId))) return false;
            if (table == "collection_subject_identification_selections" && isHorseResource
                && Text("ResourceId") is { } selectedId && !SetText("ResourceId", Map(selectedId))) return false;
            if (Text("SubjectHorseId") is { } subjectHorseId && !SetText("SubjectHorseId", Map(subjectHorseId)))
                return false;
            if (Text("RequestedByHorseId") is { } requestedByHorseId
                && !SetText("RequestedByHorseId", Map(requestedByHorseId))) return false;
            foreach (var jsonColumn in new[] { "AttributesJson", "MetadataJson" })
            {
                if (Text(jsonColumn) is not { } json) continue;
                if (!TryTransformCollectionMetadataForExecution(json, mappings, out var transformed)) return false;
                if (!SetText(jsonColumn, transformed)) return false;
            }
            return true;
        }

        if (surface != MigrationStoreSurface.EventStore) return true;
        var keyColumn = table switch
        {
            "Horses" or "HorseWeightHistories" or "HorseRaceHistories" => "HorseId",
            "JraSubjectProfileReadModel" => "SubjectId",
            "HorseIdentityRepairCandidates" or "HorseIdentityRepairRedirects" => "SourceHorseId",
            _ => null,
        };
        if (keyColumn is not null && Text(keyColumn) is { } key && !SetText(keyColumn, Map(key))) return false;
        if (table is "HorseIdentityRepairCandidates" or "HorseIdentityRepairRedirects"
            && Text("TargetHorseId") is { } targetHorseId && !SetText("TargetHorseId", Map(targetHorseId)))
            return false;
        if (table == "SubjectIdentificationRepairIssues"
            && Text("SubjectId") is { } subjectId && !SetText("SubjectId", Map(subjectId))) return false;
        if (table == "SubjectIdentificationRepairIssues"
            && Text("TargetSubjectId") is { } targetSubjectId && !SetText("TargetSubjectId", Map(targetSubjectId)))
            return false;
        if (table == "MemoSubjects" && Text("SubjectKey") is { } subjectKey)
        {
            foreach (var mapping in mappings)
            {
                var oldKey = MemoBySubjectLocator.MakeKey(MemoSubjectType.Horse, mapping.OldHorseId);
                if (string.Equals(subjectKey, oldKey, StringComparison.Ordinal))
                    if (!SetText("SubjectKey", MemoBySubjectLocator.MakeKey(MemoSubjectType.Horse,
                            mapping.DestinationHorseId))) return false;
            }
        }

        var jsonRoles = table switch
        {
            "Horses" => new[] { "Aliases" },
            "JraSubjectProfileReadModel" => new[] { "Fields" },
            "Jockeys" or "Trainers" => new[] { "Aliases" },
            "RacePredictionContexts" => new[] { "Entries", "WeatherObservations", "TrackConditionObservations", "OddsSnapshots" },
            "RaceResults" => new[] { "EntryResults", "EntryIndexes", "PayoutResult" },
            "PredictionTickets" => new[] { "Marks", "Evaluations" },
            "HorseWeightHistories" => new[] { "WeightHistory" },
            "PredictionComparisons" => new[] { "TicketStates", "EntryIndexes", "EntryResults", "PayoutResult" },
            "MemoSubjects" => new[] { "Memos" },
            "HorseRaceHistories" => new[] { "Entries" },
            "JockeyRaceHistories" => new[] { "Entries" },
            _ => [],
        };
        var keyName = table switch
        {
            "Horses" or "HorseWeightHistories" or "HorseRaceHistories" => "HorseId",
            "JraSubjectProfileReadModel" => "SubjectId",
            "Jockeys" => "JockeyId",
            "Trainers" => "TrainerId",
            "RacePredictionContexts" or "RaceResults" or "PredictionComparisons" => "RaceId",
            "PredictionTickets" => "PredictionTicketId",
            "JockeyRaceHistories" => "JockeyId",
            "MemoSubjects" => "SubjectKey",
            _ => "",
        };
        var rowKey = keyName.Length > 0 ? Text(keyName) : null;
        var raceId = Text("RaceId") ?? (table == "RacePredictionContexts" ? rowKey : null);
        foreach (var jsonColumn in jsonRoles)
        {
            if (Text(jsonColumn) is not { } json) continue;
            if (!TryTransformKnownJsonForExecution(table, jsonColumn, json, rowKey ?? "", raceId,
                    mappings, out var transformed) || !SetText(jsonColumn, transformed)) return false;
        }
        return true;
    }

    internal static async Task<IReadOnlyList<TypedHorseIdentityPhysicalRow>> ReadPhysicalRowsByRowIdsAsync(
        SqliteConnection connection,
        string table,
        IReadOnlyList<string> orderedColumns,
        IReadOnlyList<long> rowIds,
        CancellationToken cancellationToken,
        TypedHorseIdentityExecutionBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(orderedColumns);
        ArgumentNullException.ThrowIfNull(rowIds);
        if (rowIds.Count is < 1 or > PageSize
            || rowIds.Distinct().Count() != rowIds.Count || orderedColumns.Count == 0
            || table.Contains('"') || orderedColumns.Any(column => string.IsNullOrWhiteSpace(column)
                || column.Contains('"')))
            throw new InvalidDataException("Physical page definition is invalid.");

        var placeholders = rowIds.Select((_, index) => "$row" + index.ToString(
            System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var projection = string.Join(",", orderedColumns.Select(column =>
            $"CASE WHEN length(CAST(\"{column}\" AS BLOB)) <= $maxBytes THEN \"{column}\" ELSE NULL END, "
            + $"length(CAST(\"{column}\" AS BLOB))"));
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 1;
        command.CommandText = $"SELECT rowid,{projection} FROM \"{table}\" WHERE rowid IN ({string.Join(',', placeholders)}) ORDER BY rowid;";
        command.Parameters.AddWithValue("$maxBytes", MaximumSerializedBytes);
        for (var index = 0; index < rowIds.Count; index++)
            command.Parameters.AddWithValue(placeholders[index], rowIds[index]);

        var rows = new List<TypedHorseIdentityPhysicalRow>(rowIds.Count);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var rowId = reader.GetInt64(0);
            var values = new object?[orderedColumns.Count];
            long rowBytes = sizeof(long);
            for (var index = 0; index < orderedColumns.Count; index++)
            {
                var valueIndex = 1 + index * 2;
                var lengthIndex = valueIndex + 1;
                if (reader.IsDBNull(valueIndex) != reader.IsDBNull(lengthIndex))
                    throw new InvalidDataException("Physical page contains an oversized value.");
                if (!reader.IsDBNull(valueIndex))
                {
                    if (reader.GetInt64(lengthIndex) > MaximumSerializedBytes)
                        throw new InvalidDataException("Physical page value exceeds the configured bound.");
                    rowBytes += reader.GetInt64(lengthIndex);
                    values[index] = reader.GetValue(valueIndex);
                }
            }
            budget?.AddRow(rowBytes, cancellationToken);
            rows.Add(new(rowId, values));
        }
        if (!rows.Select(row => row.RowId).SequenceEqual(rowIds.Order()))
            throw new InvalidDataException("Physical page membership changed.");
        return rows;
    }

    internal async Task<IReadOnlyList<TypedHorseIdentityExecutionPage>> PrepareCanonicalExecutionPagesAsync(
        MigrationStoreSurface surface,
        SqliteConnection connection,
        IReadOnlyList<HorseIdentityMigrationMapping> mappings,
        TypedHorseIdentityExecutionBudget budget,
        CancellationToken cancellationToken)
    {
        var pages = new List<TypedHorseIdentityExecutionPage>();
        ArgumentNullException.ThrowIfNull(budget);
        var tables = new List<string>();
        await using (var inventory = connection.CreateCommand())
        {
            inventory.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await inventory.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) tables.Add(reader.GetString(0));
        }

        foreach (var table in tables)
        {
            if (table.Contains('"')) throw new InvalidDataException("Physical table identifier is unsupported.");
            var columns = new List<string>();
            await using (var schema = connection.CreateCommand())
            {
                schema.CommandText = $"PRAGMA table_info(\"{table}\");";
                await using var reader = await schema.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) columns.Add(reader.GetString(1));
            }
            if (columns.Count == 0 || columns.Any(column => column.Contains('"')))
                throw new InvalidDataException("Physical table schema is unsupported.");

            long? afterRowId = null;
            while (true)
            {
                var projection = string.Join(",", columns.Select(column =>
                    $"CASE WHEN length(CAST(\"{column}\" AS BLOB)) <= $maxBytes THEN \"{column}\" ELSE NULL END, "
                    + $"length(CAST(\"{column}\" AS BLOB))"));
                await using var command = connection.CreateCommand();
                command.CommandTimeout = 1;
                command.CommandText = $"SELECT rowid,{projection} FROM \"{table}\" "
                    + (afterRowId.HasValue ? "WHERE rowid > $after " : string.Empty)
                    + "ORDER BY rowid LIMIT $limit;";
                command.Parameters.AddWithValue("$maxBytes", MaximumSerializedBytes);
                if (afterRowId.HasValue) command.Parameters.AddWithValue("$after", afterRowId.Value);
                command.Parameters.AddWithValue("$limit", PageSize + 1);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                using var before = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var after = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var rowIds = new List<long>(PageSize);
                var eventContexts = new Dictionary<string, TypedHorseIdentityTicketStreamContext>(StringComparer.Ordinal);
                while (rowIds.Count < PageSize && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var rowId = reader.GetInt64(0);
                    var source = new object?[columns.Count];
                    long rowBytes = sizeof(long);
                    for (var index = 0; index < columns.Count; index++)
                    {
                        var valueIndex = 1 + index * 2;
                        var lengthIndex = valueIndex + 1;
                        if (reader.IsDBNull(valueIndex) != reader.IsDBNull(lengthIndex))
                            throw new InvalidDataException("Physical row exceeds the serialized-field bound.");
                        if (!reader.IsDBNull(valueIndex))
                        {
                            if (reader.GetInt64(lengthIndex) > MaximumSerializedBytes)
                                throw new InvalidDataException("Physical row exceeds the serialized-field bound.");
                            rowBytes += reader.GetInt64(lengthIndex);
                            source[index] = reader.GetValue(valueIndex);
                        }
                    }
                    var expected = source.ToArray();
                    if (surface == MigrationStoreSurface.EventStore && table == "EventEntity")
                    {
                        if (!await TryTransformEventEntityRowAsync(connection, columns, source, expected,
                                eventContexts, mappings, cancellationToken, budget).ConfigureAwait(false))
                            throw new InvalidDataException("Event row has no registered typed transformation recipe.");
                    }
                    else if (!TryTransformPhysicalRow(surface, table, columns, source, mappings, out expected))
                        throw new InvalidDataException("Physical row has no registered typed transformation recipe.");

                    Append(before, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Append(after, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    for (var index = 0; index < columns.Count; index++)
                    {
                        Append(before, columns[index]);
                        Append(after, columns[index]);
                        Append(before, StableSqlValue(source[index] ?? DBNull.Value) ?? "<null>");
                        Append(after, StableSqlValue(expected[index] ?? DBNull.Value) ?? "<null>");
                    }
                    rowIds.Add(rowId);
                    budget.AddRow(rowBytes, cancellationToken);
                }
                var hasMore = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (rowIds.Count == 0)
                {
                    if (afterRowId is null)
                    {
                        var empty = Convert.ToHexString(SHA256.HashData([]));
                        pages.Add(new(new(surface, table, 0, empty, empty, "start", "end"), ComputeMembershipHash([])));
                    }
                    break;
                }
                var evidence = new TypedHorseIdentityPageEvidence(surface, table, rowIds.Count,
                    Convert.ToHexString(before.GetHashAndReset()), Convert.ToHexString(after.GetHashAndReset()),
                    afterRowId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "start",
                    hasMore ? rowIds[^1].ToString(System.Globalization.CultureInfo.InvariantCulture) : "end");
                pages.Add(new(evidence, ComputeMembershipHash(rowIds)));
                var summaryBytes = Encoding.UTF8.GetByteCount(surface.ToString()) + Encoding.UTF8.GetByteCount(table)
                    + Encoding.UTF8.GetByteCount(evidence.BeforeHash) + Encoding.UTF8.GetByteCount(evidence.ExpectedAfterHash)
                    + Encoding.UTF8.GetByteCount(evidence.FirstKey ?? string.Empty)
                    + Encoding.UTF8.GetByteCount(evidence.LastKey ?? string.Empty) + sizeof(long) + sizeof(int);
                budget.AddPage(summaryBytes);
                if (!hasMore) break;
                afterRowId = rowIds[^1];
            }
        }
        return pages;
    }

    internal async Task<IReadOnlyList<TypedHorseIdentityTransformedRow>> ReadAndTransformExecutionPageAsync(
        SqliteConnection connection,
        TypedHorseIdentityPageEvidence evidence,
        IReadOnlyList<HorseIdentityMigrationMapping> mappings,
        CancellationToken cancellationToken,
        TypedHorseIdentityExecutionBudget? budget = null)
    {
        var initialPage = string.Equals(evidence.FirstKey, "start", StringComparison.Ordinal);
        var terminalPage = string.Equals(evidence.LastKey, "end", StringComparison.Ordinal);
        var hasAfter = long.TryParse(evidence.FirstKey, System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out var afterRowId);
        var hasThrough = long.TryParse(evidence.LastKey, System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out var throughRowId);
        if ((!initialPage && !hasAfter) || (!terminalPage && !hasThrough)
            || hasAfter && hasThrough && throughRowId <= afterRowId || evidence.RowCount is < 0 or > PageSize)
            throw new InvalidDataException("Execution page key bounds are invalid.");
        var columns = new List<string>();
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = $"PRAGMA table_info(\"{evidence.Table}\");";
            await using var reader = await schema.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) columns.Add(reader.GetString(1));
        }
        if (columns.Count == 0 || columns.Any(column => column.Contains('"')))
            throw new InvalidDataException("Execution page schema is invalid.");

        var rowIds = new List<long>((int)evidence.RowCount);
        await using (var command = connection.CreateCommand())
        {
            command.CommandTimeout = 1;
            command.CommandText = "SELECT rowid FROM \"" + evidence.Table + "\" "
                + (initialPage ? string.Empty : "WHERE rowid > $after ")
                + (initialPage && !terminalPage ? "WHERE rowid <= $through "
                    : !initialPage && !terminalPage ? "AND rowid <= $through " : string.Empty)
                + "ORDER BY rowid LIMIT $limit;";
            if (!initialPage) command.Parameters.AddWithValue("$after", afterRowId);
            if (!terminalPage) command.Parameters.AddWithValue("$through", throughRowId);
            command.Parameters.AddWithValue("$limit", PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rowIds.Add(reader.GetInt64(0));
        }
        if (rowIds.Count != evidence.RowCount || rowIds.Count > PageSize)
            throw new InvalidDataException("Execution page membership changed.");

        var sourceRows = rowIds.Count == 0 ? [] : await ReadPhysicalRowsByRowIdsAsync(connection,
            evidence.Table, columns, rowIds, cancellationToken, budget).ConfigureAwait(false);
        if (!sourceRows.Select(row => row.RowId).SequenceEqual(rowIds))
            throw new InvalidDataException("Execution page membership changed.");

        var contexts = new Dictionary<string, TypedHorseIdentityTicketStreamContext>(StringComparer.Ordinal);
        var transformed = new List<TypedHorseIdentityTransformedRow>(sourceRows.Count);
        foreach (var row in sourceRows)
        {
            var expected = row.Values.ToArray();
            if (evidence.Surface == MigrationStoreSurface.EventStore && evidence.Table == "EventEntity")
            {
                if (!await TryTransformEventEntityRowAsync(connection, columns, row.Values, expected,
                        contexts, mappings, cancellationToken, budget).ConfigureAwait(false))
                    throw new InvalidDataException("Event row has no registered typed transformation recipe.");
            }
            else if (!TryTransformPhysicalRow(evidence.Surface, evidence.Table, columns, row.Values,
                         mappings, out expected))
                throw new InvalidDataException("Physical row has no registered typed transformation recipe.");
            transformed.Add(new(row.RowId, row.Values, expected));
        }
        return transformed;
    }

    private async Task<bool> TryTransformEventEntityRowAsync(
        SqliteConnection connection,
        IReadOnlyList<string> columns,
        IReadOnlyList<object?> source,
        object?[] expected,
        Dictionary<string, TypedHorseIdentityTicketStreamContext> contexts,
        IReadOnlyList<HorseIdentityMigrationMapping> mappings,
        CancellationToken cancellationToken,
        TypedHorseIdentityExecutionBudget? budget)
    {
        var indexes = columns.Select((column, index) => (column, index))
            .ToDictionary(item => item.column, item => item.index, StringComparer.OrdinalIgnoreCase);
        string? Text(string column) => indexes.TryGetValue(column, out var index) ? source[index] as string : null;
        var aggregateId = Text("AggregateId");
        var aggregateName = Text("AggregateName");
        var data = Text("Data");
        var metadata = Text("Metadata");
        if (aggregateId is null || aggregateName is null || data is null || metadata is null
            || !indexes.TryGetValue("GlobalSequenceNumber", out var globalIndex)
            || !indexes.TryGetValue("AggregateSequenceNumber", out var sequenceIndex)
            || !indexes.TryGetValue("BatchId", out var batchIndex)) return false;

        TypedHorseIdentityTicketStreamContext? context = null;
        if (string.Equals(aggregateName, nameof(PredictionTicketAggregate), StringComparison.Ordinal)
            && Convert.ToInt32(source[sequenceIndex], System.Globalization.CultureInfo.InvariantCulture) > 1)
        {
            if (!contexts.TryGetValue(aggregateId, out context))
            {
                await using var command = connection.CreateCommand();
                command.CommandTimeout = 1;
                command.CommandText = """
                    SELECT rowid,
                        CASE WHEN length(CAST(Data AS BLOB)) <= $maxBytes THEN Data ELSE NULL END,
                        length(CAST(Data AS BLOB))
                    FROM EventEntity WHERE AggregateId=$id AND AggregateSequenceNumber=1;
                    """;
                command.Parameters.AddWithValue("$maxBytes", MaximumSerializedBytes);
                command.Parameters.AddWithValue("$id", aggregateId);
                string? created;
                await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return false;
                    if (reader.IsDBNull(1) != reader.IsDBNull(2) || reader.IsDBNull(1)
                        || reader.GetInt64(2) > MaximumSerializedBytes) return false;
                    var dataBytes = reader.GetInt64(2);
                    budget?.AddRow(sizeof(long) + dataBytes, cancellationToken);
                    created = reader.GetString(1);
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return false;
                }
                if (created is null || !TryReadTicketCreatedContext(created, aggregateId, out context)) return false;
                contexts.Add(aggregateId, context);
            }
        }
        var record = new TypedHorseIdentityEventRecord(
            Convert.ToInt64(source[globalIndex], System.Globalization.CultureInfo.InvariantCulture),
            aggregateId, aggregateName,
            Convert.ToInt32(source[sequenceIndex], System.Globalization.CultureInfo.InvariantCulture), data, metadata);
        if (!TryTransformEventForExecution(record, mappings, context, out var transformed)) return false;
        expected[globalIndex] = transformed.GlobalSequenceNumber;
        expected[indexes["AggregateId"]] = transformed.AggregateId;
        expected[indexes["AggregateName"]] = transformed.AggregateName;
        expected[sequenceIndex] = transformed.AggregateSequenceNumber;
        expected[batchIndex] = source[batchIndex];
        expected[indexes["Data"]] = transformed.Data;
        expected[indexes["Metadata"]] = transformed.Metadata;
        return true;
    }

    private static bool TryTransformCollectionMetadata(
        string json, ReadState state, out string expectedJson)
    {
        expectedJson = json;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return false;
            static string? ReadString(JsonObject value, string name) =>
                value.FirstOrDefault(property => property.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .Value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var result) ? result : null;
            static JsonNode? FindValue(JsonObject value, string name) =>
                value.FirstOrDefault(property => property.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
            var requestedByHorse = FindValue(obj, "requestedByHorseId");
            if (requestedByHorse is JsonValue requestedValue && requestedValue.TryGetValue<string>(out var requestedId)
                && requestedId is not null && state.Replacements.TryGetValue(requestedId, out var mappedRequested))
                obj[obj.First(property => property.Key.Equals("requestedByHorseId", StringComparison.OrdinalIgnoreCase)).Key]
                    = mappedRequested;

            var discoveredId = FindValue(obj, "discoveredFromId");
            if (discoveredId is JsonValue discoveredValue && discoveredValue.TryGetValue<string>(out var sourceId)
                && sourceId is not null && string.Equals(ReadString(obj, "discoveredFromType"), "Horse",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(ReadString(obj, "discoveredFromProvider"), "JRA", StringComparison.OrdinalIgnoreCase)
                && state.Replacements.TryGetValue(sourceId, out var mappedSource))
                obj[obj.First(property => property.Key.Equals("discoveredFromId", StringComparison.OrdinalIgnoreCase)).Key]
                    = mappedSource;

            var ancestorsNode = FindValue(obj, "discoveryAncestors");
            if (ancestorsNode is JsonValue ancestorsValue && ancestorsValue.TryGetValue<string>(out var ancestors)
                && ancestors is not null)
            {
                var rewritten = string.Join('|', ancestors.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => state.Replacements.GetValueOrDefault(id, id)));
                if (!string.Equals(rewritten, ancestors, StringComparison.Ordinal))
                    obj[obj.First(property => property.Key.Equals("discoveryAncestors", StringComparison.OrdinalIgnoreCase)).Key]
                        = rewritten;
            }
            expectedJson = obj.ToJsonString();
            return SameJsonShape(JsonNode.Parse(json), JsonNode.Parse(expectedJson));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<bool> InspectKeyTableAsync(
        SqliteConnection connection, ReadState state, string table, string keyColumn,
        CancellationToken cancellationToken)
    {
        long afterRowId = 0;
        var oldPresent = false;
        var destinationPresent = false;
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT rowid, CASE WHEN length(CAST(\"{keyColumn}\" AS BLOB)) <= $maxBytes "
                + $"THEN \"{keyColumn}\" ELSE NULL END, length(CAST(\"{keyColumn}\" AS BLOB)) FROM \"{table}\" "
                + "WHERE rowid > $after ORDER BY rowid LIMIT $limit;";
            command.CommandTimeout = 1;
            command.Parameters.AddWithValue("$maxBytes", MaximumSerializedBytes);
            command.Parameters.AddWithValue("$after", afterRowId);
            command.Parameters.AddWithValue("$limit", PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var expectedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long pageRows = 0;
            long first = 0;
            long last = afterRowId;
            while (pageRows < PageSize && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2)
                    || reader.GetInt64(2) > MaximumSerializedBytes) return false;
                var rowId = reader.GetInt64(0);
                if (first == 0) first = rowId;
                last = rowId;
                var id = reader.GetString(1);
                Append(hash, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(expectedHash, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(hash, id);
                Append(expectedHash, state.Replacements.GetValueOrDefault(id, id));
                oldPresent |= state.OldIds.Contains(id);
                destinationPresent |= state.DestinationIds.Contains(id);
                if (!state.AddRow(ref pageRows, ref state.InputBytes, sizeof(long) + reader.GetInt64(2))) return false;
            }
            var hasMore = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (pageRows == 0) break;
            var digest = Convert.ToHexString(hash.GetHashAndReset());
            var expectedDigest = Convert.ToHexString(expectedHash.GetHashAndReset());
            state.AddPage(MigrationStoreSurface.EventStore, table, Math.Min(pageRows, PageSize), digest,
                expectedDigest, first.ToString(System.Globalization.CultureInfo.InvariantCulture),
                last.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!hasMore) break;
            afterRowId = last;
        }
        state.Collision |= oldPresent && destinationPresent;
        return true;
    }

    private static async Task<bool> InspectMemoSubjectsAsync(
        SqliteConnection connection, ReadState state, CancellationToken cancellationToken)
    {
        long afterRowId = 0;
        var oldPresent = new HashSet<string>(StringComparer.Ordinal);
        var destinationPresent = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT rowid, CASE WHEN length(CAST(SubjectKey AS BLOB)) <= $maxBytes THEN SubjectKey ELSE NULL END,
                       length(CAST(SubjectKey AS BLOB))
                FROM MemoSubjects WHERE rowid > $after ORDER BY rowid LIMIT $limit;
                """;
            command.CommandTimeout = 1;
            command.Parameters.AddWithValue("$maxBytes", MaximumSerializedBytes);
            command.Parameters.AddWithValue("$after", afterRowId);
            command.Parameters.AddWithValue("$limit", PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var expectedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long rows = 0;
            long first = 0;
            long last = afterRowId;
            while (rows < PageSize && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2)
                    || reader.GetInt64(2) > MaximumSerializedBytes) return false;
                var rowId = reader.GetInt64(0);
                if (first == 0) first = rowId;
                last = rowId;
                var key = reader.GetString(1);
                Append(hash, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(expectedHash, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(hash, key);
                var expectedKey = key;
                foreach (var oldId in state.OldIds)
                {
                    var oldKey = MemoBySubjectLocator.MakeKey(MemoSubjectType.Horse, oldId);
                    var newKey = MemoBySubjectLocator.MakeKey(MemoSubjectType.Horse,
                        state.Replacements[oldId]);
                    if (key == oldKey) oldPresent.Add(oldId);
                    if (key == newKey) destinationPresent.Add(oldId);
                    if (key == oldKey) expectedKey = newKey;
                }
                Append(expectedHash, expectedKey);
                if (!state.AddRow(ref rows, ref state.InputBytes,
                        sizeof(long) + reader.GetInt64(2))) return false;
            }
            var hasMore = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (rows == 0) break;
            state.AddPage(MigrationStoreSurface.EventStore, "MemoSubjects", rows,
                Convert.ToHexString(hash.GetHashAndReset()), Convert.ToHexString(expectedHash.GetHashAndReset()),
                first.ToString(System.Globalization.CultureInfo.InvariantCulture),
                last.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!hasMore) break;
            afterRowId = last;
        }
        state.Collision |= oldPresent.Overlaps(destinationPresent);
        return true;
    }

    private static async Task<bool> InspectReferenceColumnsAsync(
        SqliteConnection connection, ReadState state, string table, IReadOnlyList<string> columns,
        CancellationToken cancellationToken)
    {
        var select = string.Join(",", columns.Select(column =>
            $"CASE WHEN length(CAST(\"{column}\" AS BLOB)) <= $maxBytes THEN \"{column}\" ELSE NULL END, "
            + $"length(CAST(\"{column}\" AS BLOB))"));
        var oldPresent = new HashSet<string>(StringComparer.Ordinal);
        var destinationPresent = new HashSet<string>(StringComparer.Ordinal);
        long afterRowId = 0;
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT rowid,{select} FROM \"{table}\" WHERE rowid > $after ORDER BY rowid LIMIT $limit;";
            command.CommandTimeout = 1;
            command.Parameters.AddWithValue("$maxBytes", MaximumSerializedBytes);
            command.Parameters.AddWithValue("$after", afterRowId);
            command.Parameters.AddWithValue("$limit", PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var expectedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long rows = 0;
            long first = 0;
            long last = afterRowId;
            while (rows < PageSize && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var rowId = reader.GetInt64(0);
                if (first == 0) first = rowId;
                last = rowId;
                Append(hash, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(expectedHash, rowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                long rowBytes = sizeof(long);
                for (var index = 0; index < columns.Count; index++)
                {
                    var valueIndex = 1 + index * 2;
                    var lengthIndex = valueIndex + 1;
                    var valueIsNull = reader.IsDBNull(valueIndex);
                    var lengthIsNull = reader.IsDBNull(lengthIndex);
                    if (valueIsNull != lengthIsNull) return false;
                    if (valueIsNull)
                    {
                        Append(hash, "<null>");
                        Append(expectedHash, "<null>");
                        continue;
                    }
                    var byteLength = reader.GetInt64(lengthIndex);
                    if (byteLength > MaximumSerializedBytes) return false;
                    var value = reader.GetString(valueIndex);
                    Append(hash, value);
                    Append(expectedHash, state.Replacements.GetValueOrDefault(value, value));
                    if (state.OldIds.Contains(value)) oldPresent.Add(value);
                    if (state.DestinationIds.Contains(value)) destinationPresent.Add(value);
                    rowBytes += byteLength;
                }
                if (!state.AddRow(ref rows, ref state.InputBytes, rowBytes)) return false;
            }
            var hasMore = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (rows == 0) break;
            state.AddPage(MigrationStoreSurface.EventStore, table, rows,
                Convert.ToHexString(hash.GetHashAndReset()), Convert.ToHexString(expectedHash.GetHashAndReset()),
                first.ToString(System.Globalization.CultureInfo.InvariantCulture),
                last.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!hasMore) break;
            afterRowId = last;
        }
        state.Collision |= oldPresent.Overlaps(destinationPresent);
        return true;
    }

    private async Task<bool> InspectEventEntitiesAsync(
        SqliteConnection connection, ReadState state, CancellationToken cancellationToken)
    {
        await using (var validation = connection.CreateCommand())
        {
            validation.CommandTimeout = 1;
            validation.CommandText = """
                SELECT COUNT(*) FROM (
                    SELECT AggregateId FROM EventEntity GROUP BY AggregateId
                    HAVING SUM(CASE WHEN AggregateSequenceNumber = 1 THEN 1 ELSE 0 END) <> 1
                       OR COUNT(DISTINCT AggregateSequenceNumber) <> COUNT(*)
                );
                """;
            if (Convert.ToInt64(await validation.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    System.Globalization.CultureInfo.InvariantCulture) != 0)
            {
                state.Failure ??= "event-stream-sequence-shape-invalid";
                return false;
            }
        }

        long afterStreamStartRowId = 0;
        while (true)
        {
            var streams = new List<(long FirstRowId, string AggregateId)>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT rowid, AggregateId FROM EventEntity
                    WHERE AggregateSequenceNumber = 1 AND rowid > $after
                    ORDER BY rowid LIMIT $limit;
                    """;
                command.CommandTimeout = 1;
                command.Parameters.AddWithValue("$after", afterStreamStartRowId);
                command.Parameters.AddWithValue("$limit", PageSize + 1);
                await using var starts = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (streams.Count < PageSize && await starts.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (starts.IsDBNull(0) || starts.IsDBNull(1)) return false;
                    streams.Add((starts.GetInt64(0), starts.GetString(1)));
                }
                if (streams.Count == 0) break;
                afterStreamStartRowId = streams[^1].FirstRowId;
            }

            foreach (var (_, aggregateId) in streams)
            {
                long afterEventRowId = 0;
                var sequence = 0;
                TypedHorseIdentityTicketStreamContext? ticketContext = null;
                while (true)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = """
                        SELECT rowid, GlobalSequenceNumber,
                               CASE WHEN length(CAST(AggregateId AS BLOB)) <= $maxBytes THEN AggregateId ELSE NULL END,
                               length(CAST(AggregateId AS BLOB)),
                               CASE WHEN length(CAST(AggregateName AS BLOB)) <= $maxBytes THEN AggregateName ELSE NULL END,
                               length(CAST(AggregateName AS BLOB)), AggregateSequenceNumber,
                               CASE WHEN length(CAST(BatchId AS BLOB)) <= $maxBytes THEN BatchId ELSE NULL END,
                               length(CAST(BatchId AS BLOB)),
                               CASE WHEN length(CAST(Data AS BLOB)) <= $maxBytes THEN Data ELSE NULL END,
                               length(CAST(Data AS BLOB)),
                               CASE WHEN length(CAST(Metadata AS BLOB)) <= $maxBytes THEN Metadata ELSE NULL END,
                               length(CAST(Metadata AS BLOB))
                        FROM EventEntity WHERE AggregateId = $aggregate AND rowid > $after
                        ORDER BY rowid LIMIT $limit;
                        """;
                    command.CommandTimeout = 1;
                    command.Parameters.AddWithValue("$maxBytes", MaximumSerializedBytes);
                    command.Parameters.AddWithValue("$aggregate", aggregateId);
                    command.Parameters.AddWithValue("$after", afterEventRowId);
                    command.Parameters.AddWithValue("$limit", PageSize + 1);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    using var expectedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long pageRows = 0;
                    long firstRowId = 0;
                    long lastRowId = afterEventRowId;
                    while (pageRows < PageSize && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (Enumerable.Range(0, reader.FieldCount).Any(reader.IsDBNull)) return false;
                        var rowId = reader.GetInt64(0);
                        if (firstRowId == 0) firstRowId = rowId;
                        lastRowId = rowId;
                        var globalSequence = reader.GetInt64(1);
                        var rowAggregateId = reader.GetString(2);
                        var aggregateIdBytes = reader.GetInt64(3);
                        var aggregateName = reader.GetString(4);
                        var aggregateNameBytes = reader.GetInt64(5);
                        var aggregateSequence = reader.GetInt32(6);
                        var batchId = reader.GetString(7);
                        var batchIdBytes = reader.GetInt64(8);
                        var data = reader.GetString(9);
                        var dataBytes = reader.GetInt64(10);
                        var metadata = reader.GetString(11);
                        var metadataBytes = reader.GetInt64(12);
                        if (rowAggregateId != aggregateId || aggregateSequence != sequence + 1
                            || aggregateIdBytes > MaximumSerializedBytes || aggregateNameBytes > MaximumSerializedBytes
                            || batchIdBytes > MaximumSerializedBytes || dataBytes > MaximumSerializedBytes
                            || metadataBytes > MaximumSerializedBytes
                            || !ValidateJson(data) || !ValidateJson(metadata)) return false;
                        sequence = aggregateSequence;
                        TypedHorseIdentityEventRecord expectedRecord = new(globalSequence, rowAggregateId,
                            aggregateName, aggregateSequence, data, metadata);
                        var isTicketAggregate = string.Equals(aggregateName,
                            nameof(PredictionTicketAggregate), StringComparison.Ordinal);
                        var isTicketCreated = string.Equals(ReadJsonString(metadata, "event_name"),
                            nameof(PredictionTicketCreated), StringComparison.Ordinal);
                        if (isTicketAggregate)
                        {
                            if (aggregateSequence == 1)
                            {
                                if (!isTicketCreated || !TryReadTicketCreatedContext(data, aggregateId,
                                        out var createdContext))
                                {
                                    state.Failure ??= "ticket-created-context-invalid";
                                    return false;
                                }
                                ticketContext = createdContext;
                            }
                            else if (isTicketCreated || ticketContext is null
                                || !string.Equals(ticketContext.AggregateId, aggregateId, StringComparison.Ordinal))
                            {
                                state.Failure ??= "ticket-created-context-invalid";
                                return false;
                            }
                        }
                        else ticketContext = null;

                        if (_eventJsonSerializer is null || _domainEventFactory is null)
                            state.Failure ??= "registered-event-codec-unavailable";
                        else
                        {
                            var record = new TypedHorseIdentityEventRecord(globalSequence, rowAggregateId,
                                aggregateName, aggregateSequence, data, metadata);
                            var transform = new TypedHorseIdentityEventTransformer(_eventJsonSerializer,
                                _domainEventFactory, state.Replacements).Transform(record,
                                isTicketAggregate && aggregateSequence > 1 ? ticketContext : null);
                            if ((!transform.IsAccepted || transform.Event is null)
                                && !IsKnownUnchangedSubjectEvent(record))
                                state.Failure ??= "registered-event-codec-rejected";
                            else if (transform.IsAccepted && transform.Event is not null)
                                expectedRecord = transform.Event;
                        }

                        var stableKey = rowId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        Append(hash, stableKey);
                        Append(expectedHash, stableKey);
                        AppendEventRow(hash, globalSequence, rowAggregateId, aggregateName,
                            aggregateSequence, batchId, data, metadata);
                        AppendEventRow(expectedHash, expectedRecord.GlobalSequenceNumber,
                            expectedRecord.AggregateId, expectedRecord.AggregateName,
                            expectedRecord.AggregateSequenceNumber, batchId,
                            expectedRecord.Data, expectedRecord.Metadata);
                        if (!state.AddRow(ref pageRows, ref state.InputBytes,
                                aggregateIdBytes + aggregateNameBytes + batchIdBytes + dataBytes + metadataBytes
                                + Encoding.UTF8.GetByteCount(globalSequence.ToString(
                                    System.Globalization.CultureInfo.InvariantCulture))
                                + Encoding.UTF8.GetByteCount(aggregateSequence.ToString(
                                    System.Globalization.CultureInfo.InvariantCulture)))) return false;
                    }
                    var hasMore = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    if (pageRows == 0) break;
                    state.AddPage(MigrationStoreSurface.EventStore, "EventEntity", pageRows,
                        Convert.ToHexString(hash.GetHashAndReset()),
                        Convert.ToHexString(expectedHash.GetHashAndReset()),
                        firstRowId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        lastRowId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (!hasMore) break;
                    afterEventRowId = lastRowId;
                }
            }
        }
        return true;
    }

    private static bool TryReadTicketCreatedContext(
        string data, string aggregateId, out TypedHorseIdentityTicketStreamContext context)
    {
        context = null!;
        var raceId = ReadJsonString(data, "race_id") ?? ReadJsonString(data, "raceId");
        if (string.IsNullOrWhiteSpace(raceId) || Encoding.UTF8.GetByteCount(raceId) > MaximumSerializedBytes)
            return false;
        context = new(aggregateId, 1, raceId);
        return true;
    }

    private static void AppendEventRow(IncrementalHash hash, long globalSequenceNumber,
        string aggregateId, string aggregateName, int aggregateSequenceNumber,
        string batchId, string data, string metadata)
    {
        foreach (var (column, value) in new[]
        {
            ("GlobalSequenceNumber", "integer:" + globalSequenceNumber.ToString(
                System.Globalization.CultureInfo.InvariantCulture)),
            ("AggregateId", "text:" + aggregateId),
            ("AggregateName", "text:" + aggregateName),
            ("AggregateSequenceNumber", "integer:" + aggregateSequenceNumber.ToString(
                System.Globalization.CultureInfo.InvariantCulture)),
            ("BatchId", "text:" + batchId),
            ("Data", "text:" + data),
            ("Metadata", "text:" + metadata),
        })
        {
            Append(hash, column);
            Append(hash, value);
        }
    }

    private bool IsKnownUnchangedSubjectEvent(TypedHorseIdentityEventRecord record)
    {
        try
        {
            var domainEvent = _eventJsonSerializer!.Deserialize(record.Data, record.Metadata);
            var eventType = domainEvent.GetAggregateEvent().GetType();
            var aggregateType = domainEvent.AggregateType;
            var supportedType = string.Equals(record.AggregateName, nameof(JockeyAggregate), StringComparison.Ordinal)
                && aggregateType == typeof(JockeyAggregate)
                && eventType is not null && JockeyEventsWithoutHorseReferences.Contains(eventType)
                || string.Equals(record.AggregateName, nameof(TrainerAggregate), StringComparison.Ordinal)
                && aggregateType == typeof(TrainerAggregate)
                && eventType is not null && TrainerEventsWithoutHorseReferences.Contains(eventType);
            if (!supportedType || domainEvent.Metadata.EventVersion != 1
                || !string.Equals(ReadJsonString(record.Metadata, "aggregate_id"), record.AggregateId,
                    StringComparison.Ordinal)
                || !string.Equals(ReadJsonString(record.Metadata, "aggregate_name"), record.AggregateName,
                    StringComparison.Ordinal)
                || !int.TryParse(ReadJsonString(record.Metadata, "aggregate_sequence_number"),
                    System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                    out var metadataSequence)
                || metadataSequence != record.AggregateSequenceNumber)
                return false;

            var canonical = _eventJsonSerializer.Serialize(domainEvent);
            return SameJsonValues(record.Data, canonical.SerializedData)
                && SameJsonValues(record.Metadata, canonical.SerializedMetadata);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static readonly HashSet<Type> JockeyEventsWithoutHorseReferences =
    [
        typeof(JockeyRegistered), typeof(JockeyProfileUpdated), typeof(JockeyAliasMerged),
        typeof(JockeyDataCorrected),
    ];

    private static readonly HashSet<Type> TrainerEventsWithoutHorseReferences =
    [
        typeof(TrainerRegistered), typeof(TrainerProfileUpdated), typeof(TrainerAliasMerged),
        typeof(TrainerDataCorrected), typeof(TrainerJraProfileCollected),
    ];

    private static bool SameJsonValues(string left, string right) =>
        JsonNode.DeepEquals(JsonNode.Parse(left), JsonNode.Parse(right));

    private static string? ReadJsonString(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        foreach (var property in document.RootElement.EnumerateObject())
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        return null;
    }

    private static bool ValidateJson(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumSerializedBytes) return false;
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        return !HasDuplicateProperties(document.RootElement);
    }

    private static bool ContainsMappedTypedReference(string json, ReadState state)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        return ContainsMappedTypedReference(document.RootElement, state);
    }

    private static bool ContainsMappedTypedReference(JsonElement element, ReadState state)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (TypedJsonNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() is { } value && state.OldIds.Contains(value))
                    return true;
                if (ContainsMappedTypedReference(property.Value, state)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                if (ContainsMappedTypedReference(child, state)) return true;
        }
        return false;
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateProperties(property.Value)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                if (HasDuplicateProperties(child)) return true;
        }
        return false;
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private sealed class ReadState(
        List<TypedHorseIdentityPageEvidence> pages,
        HashSet<string> oldIds,
        HashSet<string> destinationIds,
        IReadOnlyDictionary<string, string> replacements,
        Stopwatch stopwatch,
        long inspectedRows,
        long inputBytes,
        HorseIdentityMigrationInspectionProgress? inspectionProgress)
    {
        internal readonly List<TypedHorseIdentityPageEvidence> Pages = pages;
        internal readonly HashSet<string> OldIds = oldIds;
        internal readonly HashSet<string> DestinationIds = destinationIds;
        internal readonly IReadOnlyDictionary<string, string> Replacements = replacements;
        internal readonly Stopwatch Stopwatch = stopwatch;
        internal long InspectedRows = inspectedRows;
        internal long InputBytes = inputBytes;
        internal long SummaryBytes;
        internal bool Collision;
        internal string? Failure;
        private readonly HorseIdentityMigrationInspectionProgress? _inspectionProgress = inspectionProgress;

        internal bool AddRow(ref long pageRows, ref long bytes, long rowBytes)
        {
            pageRows++;
            InspectedRows++;
            bytes += Math.Max(0, rowBytes);
            return InspectedRows <= MaximumRows && bytes <= MaximumInputBytes
                && Stopwatch.Elapsed <= MaximumDuration;
        }

        internal void AddPage(MigrationStoreSurface surface, string table, long rows,
            string beforeHash, string expectedAfterHash, string? firstKey = null, string? lastKey = null)
        {
            // Page evidence is immutable and bounded; the source rows are never retained.
            Pages.Add(new(surface, table, rows, beforeHash, expectedAfterHash, firstKey, lastKey));
            _inspectionProgress?.PageCompleted(surface, table, InspectedRows, InputBytes);
            SummaryBytes += Encoding.UTF8.GetByteCount(surface.ToString()) + Encoding.UTF8.GetByteCount(table)
                + Encoding.UTF8.GetByteCount(beforeHash) + Encoding.UTF8.GetByteCount(expectedAfterHash)
                + (firstKey is null ? 0 : Encoding.UTF8.GetByteCount(firstKey))
                + (lastKey is null ? 0 : Encoding.UTF8.GetByteCount(lastKey))
                + sizeof(long) + sizeof(int);
            if (SummaryBytes > MaximumSummaryBytes) Failure = "summary-budget-exceeded";
        }

        internal TypedHorseIdentityReferenceReadResult Result(
            TypedHorseIdentityReferenceReadStatus status, string? failure = null) =>
            new(status == TypedHorseIdentityReferenceReadStatus.Complete && (failure ?? Failure) is not null
                    ? TypedHorseIdentityReferenceReadStatus.Incomplete : status,
                Pages.ToArray(), InspectedRows, InputBytes, failure ?? Failure);
    }
}
