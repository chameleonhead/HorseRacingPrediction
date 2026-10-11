using System.Data;
using System.Globalization;
using System.Text.Json;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.EventStores;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HorseRacingPrediction.Api.Identity;

internal sealed class IdentityMigrationPreflightEventStoreReader(
    IDbContextProvider<EventStoreDbContext> provider)
{
    private const int CommandTimeoutSeconds = 1;
    private const int RegistrationLimitPerHorse = 2;
    private const int LaterEventLimitPerHorse = 16;
    private const int LaterEventOverfetchPerHorse = LaterEventLimitPerHorse + 1;
    private const int SourceLimitPerHorse = 16;
    private const int MaxNameCharacters = 128;
    private const int MaxReferenceRows = 16;

    private static readonly IReadOnlyDictionary<string, string[]> RequiredColumns =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Horses"] = ["HorseId", "RegisteredName", "NormalizedName", "BirthDate"],
            ["EventEntity"] = ["GlobalSequenceNumber", "AggregateId", "AggregateName", "AggregateSequenceNumber", "Data", "Metadata"],
            ["JraSubjectProfileReadModel"] = ["SubjectId", "SourceIdentity", "SourceUrl"],
            ["HorseRaceHistories"] = ["HorseId", "Entries"],
            ["HorseWeightHistories"] = ["HorseId", "WeightHistory"],
        };

    internal async Task<IdentityMigrationPreflightSchemaObservation> InspectSchemaAsync(
        CancellationToken cancellationToken)
    {
        using var configuredContext = provider.CreateContext();
        var connectionString = configuredContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Event store is unavailable.");
        var knownTables = configuredContext.Model.GetEntityTypes().Select(entity => entity.GetTableName())
            .Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>()
            .Concat(configuredContext.Model.GetEntityTypes().SelectMany(entity => entity.GetIndexes())
                .Select(index => index.GetDatabaseName()).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>())
            .Append("__EFMigrationsHistory")
            .Append("IX_RacePredictionContexts_RaceDate_RaceNumber")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var path = GetDataSource(connectionString);
        return await IdentityMigrationPreflightSqliteSchema.InspectAsync(
            IdentityMigrationPreflightSurface.EventStore, path, knownTables, RequiredColumns,
            RequiredColumns.Keys.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IdentityMigrationPreflightScannedRows> ReadPhaseAsync(
        IdentityMigrationPreflightPhase phase, string? afterKey,
        IReadOnlyDictionary<string, IdentityMigrationPreflightCollectionSource>? collectionSources,
        CancellationToken cancellationToken)
    {
        var (table, keyColumn, predicate) = phase switch
        {
            IdentityMigrationPreflightPhase.HorseRoots => ("Horses", "HorseId", "1=1"),
            IdentityMigrationPreflightPhase.HorseEvents => ("EventEntity", "AggregateId", "AggregateName = $aggregateName"),
            IdentityMigrationPreflightPhase.JraProfiles => ("JraSubjectProfileReadModel", "SubjectId", "1=1"),
            IdentityMigrationPreflightPhase.HorseRaceHistories => ("HorseRaceHistories", "HorseId", "1=1"),
            IdentityMigrationPreflightPhase.HorseWeightHistories => ("HorseWeightHistories", "HorseId", "1=1"),
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };

        using var configuredContext = provider.CreateContext();
        var connectionString = configuredContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Event store is unavailable.");
        await using var connection = await IdentityMigrationPreflightSqliteSchema.OpenReadOnlyAsync(
            GetDataSource(connectionString), cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);

        var keyPage = await ReadKeysAsync(connection, (SqliteTransaction)transaction, table, keyColumn,
            predicate, afterKey, cancellationToken).ConfigureAwait(false);
        var keys = keyPage.Keys;
        var scanned = await ReadEvidenceForKeysAsync(connection, (SqliteTransaction)transaction, phase,
            keys, collectionSources, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return scanned with
        {
            RowsInspected = scanned.RowsInspected + keyPage.OverlongCount,
            UnknownIdentifierCount = scanned.UnknownIdentifierCount + keyPage.OverlongCount,
            ContainsTruncation = scanned.ContainsTruncation || keyPage.OverlongCount > 0,
            HasMore = !keyPage.HasUnsafeKey && keyPage.HasMore,
            NoSafeContinuation = keyPage.HasUnsafeKey,
            LastKey = keyPage.HasUnsafeKey ? null : keys.Count == 0 ? afterKey : keys[^1],
        };
    }

    internal async Task<IdentityMigrationPreflightScannedRows> ReadCollectionPhaseEvidenceAsync(
        IReadOnlyList<string> keys,
        IReadOnlyDictionary<string, IdentityMigrationPreflightCollectionSource> collectionSources,
        CancellationToken cancellationToken)
    {
        using var configuredContext = provider.CreateContext();
        var connectionString = configuredContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Event store is unavailable.");
        await using var connection = await IdentityMigrationPreflightSqliteSchema.OpenReadOnlyAsync(
            GetDataSource(connectionString), cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
        var evidence = await ReadEvidenceForKeysAsync(connection, (SqliteTransaction)transaction,
            IdentityMigrationPreflightPhase.CollectionResources, keys, collectionSources,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return evidence;
    }

    private static async Task<KeyPage> ReadKeysAsync(SqliteConnection connection,
        SqliteTransaction transaction, string table, string keyColumn, string predicate, string? afterKey,
        CancellationToken cancellationToken)
    {
        var output = new List<(string? Key, bool Unsafe)>(IdentityMigrationPreflightPolicy.PageSize + 1);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = $"""
            SELECT CASE WHEN length(CAST({keyColumn} AS BLOB)) <= $maxKeyBytes THEN {keyColumn} ELSE NULL END,
                   length(CAST({keyColumn} AS BLOB))
            FROM {table}
            WHERE {predicate}
              AND ($after IS NULL OR {keyColumn} COLLATE BINARY > $after)
            GROUP BY {keyColumn} COLLATE BINARY
            ORDER BY {keyColumn} COLLATE BINARY
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$after", (object?)afterKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", IdentityMigrationPreflightPolicy.PageSize + 1);
        command.Parameters.AddWithValue("$maxKeyBytes", IdentityMigrationPreflightPolicy.MaximumKeyCharacters * 4);
        if (predicate != "1=1") command.Parameters.AddWithValue("$aggregateName", nameof(HorseAggregate));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var length = reader.IsDBNull(1) ? long.MaxValue : reader.GetInt64(1);
            var key = reader.IsDBNull(0) ? null : reader.GetString(0);
            var unsafeKey = key is null || key.Length > IdentityMigrationPreflightPolicy.MaximumKeyCharacters
                || length > IdentityMigrationPreflightPolicy.MaximumKeyCharacters * 4;
            output.Add((key, unsafeKey));
        }
        var hasMore = output.Count > IdentityMigrationPreflightPolicy.PageSize;
        if (hasMore) output.RemoveAt(output.Count - 1);
        var unsafeKeys = output.Count(item => item.Unsafe);
        return new(output.Where(item => !item.Unsafe).Select(item => item.Key!).ToList(), hasMore,
            unsafeKeys > 0, unsafeKeys);
    }

    private static async Task<IdentityMigrationPreflightScannedRows> ReadEvidenceForKeysAsync(
        SqliteConnection connection, SqliteTransaction transaction, IdentityMigrationPreflightPhase phase,
        IReadOnlyList<string> rawKeys,
        IReadOnlyDictionary<string, IdentityMigrationPreflightCollectionSource>? collectionSources,
        CancellationToken cancellationToken)
    {
        var keys = rawKeys.Where(IdentityMigrationPreflightPolicy.IsCanonicalHorseId).ToArray();
        var unknownIds = rawKeys.Count - keys.Length;
        if (keys.Length == 0)
            return new(rawKeys.Count, unknownIds, [], false, null);

        var current = await ReadCurrentHorsesAsync(connection, transaction, keys, cancellationToken).ConfigureAwait(false);
        var profiles = await ReadProfilesAsync(connection, transaction, keys, cancellationToken).ConfigureAwait(false);
        var registrations = await ReadRegistrationsAsync(connection, transaction, keys, cancellationToken).ConfigureAwait(false);
        var raceReferences = phase is IdentityMigrationPreflightPhase.HorseRaceHistories
            or IdentityMigrationPreflightPhase.HorseWeightHistories
            ? await ReadRaceReferencesAsync(connection, transaction, phase, keys, cancellationToken)
                .ConfigureAwait(false)
            : new Dictionary<string, RaceReferenceEvidence>(StringComparer.Ordinal);
        var output = new List<IdentityMigrationPreflightHorseEvidence>(keys.Length);
        var truncatedAny = false;
        foreach (var horseId in keys)
        {
            current.TryGetValue(horseId, out var currentHorse);
            profiles.TryGetValue(horseId, out var profile);
            registrations.TryGetValue(horseId, out var registration);
            raceReferences.TryGetValue(horseId, out var references);
            IdentityMigrationPreflightCollectionSource? collectionSource = null;
            collectionSources?.TryGetValue(horseId, out collectionSource);

            var status = IdentityMigrationPreflightPolicy.ClassifyIdentityEvidence(
                currentHorse is not null,
                profile?.Identity,
                collectionSource?.CanonicalIdentity);
            status = MergeStatus(status, currentHorse?.Status);
            status = MergeStatus(status, registration?.Status);
            status = MergeStatus(status, profile?.Status);
            status = MergeStatus(status, collectionSource?.Status);
            status = MergeStatus(status, references?.Status);

            var canonicalIdentity = GetUnambiguousIdentity(profile?.Identity, collectionSource?.CanonicalIdentity);
            var identityStatus = IdentityMigrationPreflightPolicy.ClassifyIdentityEvidence(
                currentHorse is not null, profile?.Identity, collectionSource?.CanonicalIdentity);
            output.Add(new IdentityMigrationPreflightHorseEvidence(
                horseId,
                phase,
                status,
                currentHorse?.RegisteredName,
                currentHorse?.NormalizedName,
                currentHorse?.BirthDate,
                registration?.RegisteredName,
                registration?.NormalizedName,
                registration?.BirthDate,
                canonicalIdentity,
                identityStatus,
                references?.Races ?? [],
                references?.RaceCount ?? 0,
                references?.Truncated ?? false));
            truncatedAny |= status == IdentityMigrationPreflightObservationStatus.Truncated;
        }

        return new(rawKeys.Count, unknownIds, output, truncatedAny, null);
    }

    private static IdentityMigrationPreflightObservationStatus MergeStatus(
        IdentityMigrationPreflightObservationStatus current,
        IdentityMigrationPreflightObservationStatus? additional) => additional is { } value
        && StatusPrecedence(value) > StatusPrecedence(current) ? value : current;

    private static int StatusPrecedence(IdentityMigrationPreflightObservationStatus status) => status switch
    {
        IdentityMigrationPreflightObservationStatus.Truncated => 5,
        IdentityMigrationPreflightObservationStatus.Conflict => 4,
        IdentityMigrationPreflightObservationStatus.UnknownShape => 3,
        IdentityMigrationPreflightObservationStatus.Unverifiable => 2,
        IdentityMigrationPreflightObservationStatus.Observed => 1,
        _ => 0,
    };

    private static async Task<Dictionary<string, CurrentHorse>> ReadCurrentHorsesAsync(
        SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, CurrentHorse>(StringComparer.Ordinal);
        await using var command = CreateInCommand(connection, transaction, keys, """
            SELECT HorseId,
                   CASE WHEN length(CAST(RegisteredName AS BLOB)) <= $maxBytes AND length(RegisteredName) <= $maxChars THEN RegisteredName ELSE NULL END,
                   length(CAST(RegisteredName AS BLOB)),
                   CASE WHEN length(CAST(NormalizedName AS BLOB)) <= $maxBytes AND length(NormalizedName) <= $maxChars THEN NormalizedName ELSE NULL END,
                   length(CAST(NormalizedName AS BLOB)), BirthDate
            FROM Horses WHERE HorseId IN ({ids}) ORDER BY HorseId COLLATE BINARY;
            """);
        command.Parameters.AddWithValue("$maxBytes", IdentityMigrationPreflightPolicy.MaximumPayloadBytes);
        command.Parameters.AddWithValue("$maxChars", MaxNameCharacters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            var tooLarge = reader.GetInt64(2) > IdentityMigrationPreflightPolicy.MaximumPayloadBytes
                || reader.GetInt64(4) > IdentityMigrationPreflightPolicy.MaximumPayloadBytes
                || reader.IsDBNull(1) || reader.IsDBNull(3);
            if (tooLarge)
            {
                result[id] = new(IdentityMigrationPreflightObservationStatus.Truncated, null, null, null);
                continue;
            }
            var registered = SafeName(reader.GetString(1), out var badRegistered);
            var normalized = SafeName(reader.GetString(3), out var badNormalized);
            if (badRegistered || badNormalized)
            {
                result[id] = new(IdentityMigrationPreflightObservationStatus.Truncated, null, null, null);
                continue;
            }
            DateOnly? birth = null;
            if (!reader.IsDBNull(5))
            {
                var value = reader.GetString(5);
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed))
                {
                    result[id] = new(IdentityMigrationPreflightObservationStatus.UnknownShape, null, null, null);
                    continue;
                }
                birth = parsed;
            }
            result[id] = new(IdentityMigrationPreflightObservationStatus.Observed, registered, normalized, birth);
        }
        return result;
    }

    private static async Task<Dictionary<string, CurrentProfile>> ReadProfilesAsync(
        SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, CurrentProfile>(StringComparer.Ordinal);
        await using var command = CreateInCommand(connection, transaction, keys, """
            SELECT SubjectId,
                   CASE WHEN length(CAST(SourceIdentity AS BLOB)) <= $maxBytes THEN SourceIdentity ELSE NULL END,
                   length(CAST(SourceIdentity AS BLOB)),
                   CASE WHEN length(CAST(SourceUrl AS BLOB)) <= $maxBytes THEN SourceUrl ELSE NULL END,
                   length(CAST(SourceUrl AS BLOB))
            FROM JraSubjectProfileReadModel WHERE SubjectId IN ({ids}) ORDER BY SubjectId COLLATE BINARY;
            """);
        command.Parameters.AddWithValue("$maxBytes", IdentityMigrationPreflightPolicy.MaximumPayloadBytes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            if (reader.GetInt64(2) > IdentityMigrationPreflightPolicy.MaximumPayloadBytes
                || reader.GetInt64(4) > IdentityMigrationPreflightPolicy.MaximumPayloadBytes)
            {
                result[id] = new(null, IdentityMigrationPreflightObservationStatus.Truncated);
                continue;
            }
            var identity = reader.IsDBNull(1) ? null : reader.GetString(1);
            var url = reader.IsDBNull(3) ? null : reader.GetString(3);
            var normalized = NormalizeSource(identity, url, out var valid);
            result[id] = valid
                ? new(normalized, normalized is null
                    ? IdentityMigrationPreflightObservationStatus.Unverifiable
                    : IdentityMigrationPreflightObservationStatus.Observed)
                : new(null, IdentityMigrationPreflightObservationStatus.UnknownShape);
        }
        return result;
    }

    private static async Task<Dictionary<string, Registration>> ReadRegistrationsAsync(
        SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var output = keys.ToDictionary(key => key, _ => new List<RegistrationRow>(RegistrationLimitPerHorse),
            StringComparer.Ordinal);
        await using var command = CreateInCommand(connection, transaction, keys, """
            SELECT AggregateId, AggregateName,
                   CASE WHEN length(CAST(Data AS BLOB)) <= $maxBytes THEN Data ELSE NULL END,
                   length(CAST(Data AS BLOB)),
                   CASE WHEN length(CAST(Metadata AS BLOB)) <= $maxBytes THEN Metadata ELSE NULL END,
                   length(CAST(Metadata AS BLOB))
            FROM EventEntity
            WHERE AggregateId IN ({ids}) AND AggregateName = $aggregateName AND AggregateSequenceNumber = 1
            ORDER BY AggregateId COLLATE BINARY, GlobalSequenceNumber
            LIMIT $rowLimit;
            """);
        command.Parameters.AddWithValue("$maxBytes", IdentityMigrationPreflightPolicy.MaximumPayloadBytes);
        command.Parameters.AddWithValue("$aggregateName", nameof(HorseAggregate));
        command.Parameters.AddWithValue("$rowLimit", keys.Count * RegistrationLimitPerHorse + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetString(0);
                if (!output.TryGetValue(id, out var rows)) continue;
                rows.Add(new(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5)));
            }
        }

        // This diagnostic intentionally interprets only the initial registration event.
        // Later HorseAggregate events are sampled with bounded per-aggregate overfetch and
        // surfaced as unsupported evidence rather than silently treated as absent.
        var laterEvents = await ReadLaterHorseEventsAsync(connection, transaction, keys, cancellationToken)
            .ConfigureAwait(false);

        var registrations = new Dictionary<string, Registration>(StringComparer.Ordinal);
        foreach (var (id, rows) in output)
        {
            if (rows.Count == 0) continue;
            if (rows.Count > 1)
            {
                registrations[id] = new(IdentityMigrationPreflightObservationStatus.Conflict, null, null, null);
                continue;
            }
            var row = rows[0];
            if (row.DataBytes > IdentityMigrationPreflightPolicy.MaximumPayloadBytes
                || row.MetadataBytes > IdentityMigrationPreflightPolicy.MaximumPayloadBytes
                || row.Data is null || row.Metadata is null)
            {
                registrations[id] = new(IdentityMigrationPreflightObservationStatus.Truncated, null, null, null);
                continue;
            }
            var truncated = false;
            if (!string.Equals(row.AggregateName, nameof(HorseAggregate), StringComparison.Ordinal)
                || !TryValidateMetadata(row.Metadata)
                || !TryReadRegistration(row.Data, out var registeredName, out var normalizedName,
                    out var birthDate, out truncated))
            {
                registrations[id] = new(truncated ? IdentityMigrationPreflightObservationStatus.Truncated
                    : IdentityMigrationPreflightObservationStatus.UnknownShape, null, null, null);
                continue;
            }
            registrations[id] = new(IdentityMigrationPreflightObservationStatus.Observed,
                registeredName, normalizedName, birthDate);
        }

        foreach (var (id, laterEventCount) in laterEvents)
        {
            var current = registrations.GetValueOrDefault(id);
            if (current?.Status is IdentityMigrationPreflightObservationStatus.Conflict
                or IdentityMigrationPreflightObservationStatus.Truncated)
                continue;
            registrations[id] = new(laterEventCount > LaterEventLimitPerHorse
                    ? IdentityMigrationPreflightObservationStatus.Truncated
                    : IdentityMigrationPreflightObservationStatus.UnknownShape,
                current?.RegisteredName, current?.NormalizedName, current?.BirthDate);
        }
        return registrations;
    }

    private static async Task<Dictionary<string, int>> ReadLaterHorseEventsAsync(
        SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (keys.Count == 0) return counts;

        var branches = new string[keys.Count];
        for (var index = 0; index < keys.Count; index++)
        {
            var idParameter = "$id" + index;
            branches[index] = $"""
                SELECT AggregateId, AggregateSequenceNumber FROM (
                    SELECT AggregateId, AggregateSequenceNumber
                    FROM EventEntity
                    WHERE AggregateId = {idParameter} AND AggregateName = $aggregateName
                        AND AggregateSequenceNumber > 1
                    ORDER BY AggregateSequenceNumber
                    LIMIT $perAggregateLimit
                )
                """;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = string.Join(" UNION ALL ", branches);
        command.Parameters.AddWithValue("$aggregateName", nameof(HorseAggregate));
        command.Parameters.AddWithValue("$perAggregateLimit", LaterEventOverfetchPerHorse);
        for (var index = 0; index < keys.Count; index++)
            command.Parameters.AddWithValue("$id" + index, keys[index]);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        return counts;
    }

    private static async Task<Dictionary<string, RaceReferenceEvidence>> ReadRaceReferencesAsync(
        SqliteConnection connection, SqliteTransaction transaction, IdentityMigrationPreflightPhase phase,
        IReadOnlyList<string> horseIds, CancellationToken cancellationToken)
    {
        var column = phase == IdentityMigrationPreflightPhase.HorseRaceHistories ? "Entries" : "WeightHistory";
        var racesByHorse = horseIds.ToDictionary(id => id, _ => new List<string>(MaxReferenceRows),
            StringComparer.Ordinal);
        var invalid = new HashSet<string>(StringComparer.Ordinal);
        var truncated = new HashSet<string>(StringComparer.Ordinal);
        var idParameters = horseIds.Select((_, index) => "$id" + index).ToArray();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = $"""
                SELECT HorseId,
                       CASE WHEN length(CAST({column} AS BLOB)) <= $maxBytes THEN {column} ELSE NULL END,
                       length(CAST({column} AS BLOB))
                FROM {(phase == IdentityMigrationPreflightPhase.HorseRaceHistories ? "HorseRaceHistories" : "HorseWeightHistories")}
                WHERE HorseId IN ({string.Join(",", idParameters)})
                ORDER BY HorseId COLLATE BINARY;
                """;
            command.Parameters.AddWithValue("$maxBytes", IdentityMigrationPreflightPolicy.MaximumPayloadBytes);
            for (var index = 0; index < horseIds.Count; index++)
                command.Parameters.AddWithValue(idParameters[index], horseIds[index]);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var horseId = reader.GetString(0);
                if (reader.GetInt64(2) > IdentityMigrationPreflightPolicy.MaximumPayloadBytes
                    || reader.IsDBNull(1))
                {
                    truncated.Add(horseId);
                    continue;
                }

                if (!TryReadRaceIds(reader.GetString(1), out var raceIds, out var wasTruncated))
                {
                    invalid.Add(horseId);
                    continue;
                }
                if (wasTruncated) truncated.Add(horseId);
                racesByHorse[horseId].AddRange(raceIds);
            }
        }

        var result = new Dictionary<string, RaceReferenceEvidence>(StringComparer.Ordinal);
        foreach (var horseId in horseIds)
        {
            var state = truncated.Contains(horseId)
                ? IdentityMigrationPreflightObservationStatus.Truncated
                : invalid.Contains(horseId)
                    ? IdentityMigrationPreflightObservationStatus.UnknownShape
                    : IdentityMigrationPreflightObservationStatus.Observed;
            var allRaceIds = racesByHorse[horseId].Distinct(StringComparer.Ordinal).ToArray();
            var tooMany = allRaceIds.Length > MaxReferenceRows;
            if (tooMany) state = IdentityMigrationPreflightObservationStatus.Truncated;
            else if (allRaceIds.Length > 0 && state == IdentityMigrationPreflightObservationStatus.Observed)
                state = IdentityMigrationPreflightObservationStatus.Unverifiable;
            result[horseId] = new([], allRaceIds.Length, tooMany || truncated.Contains(horseId), state);
        }
        return result;
    }

    private static bool TryReadRaceIds(string json, out List<string> raceIds, out bool truncated)
    {
        raceIds = [];
        truncated = false;
        try
        {
            using var document = JsonDocument.Parse(json,
                new JsonDocumentOptions { MaxDepth = IdentityMigrationPreflightPolicy.MaximumJsonDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (raceIds.Count == MaxReferenceRows)
                {
                    truncated = true;
                    break;
                }
                if (item.ValueKind != JsonValueKind.Object) return false;
                string? raceId = null;
                var foundRaceId = false;
                foreach (var property in item.EnumerateObject())
                {
                    if (property.Name.Equals("RaceId", StringComparison.OrdinalIgnoreCase))
                    {
                        if (foundRaceId || property.Value.ValueKind != JsonValueKind.String) return false;
                        foundRaceId = true;
                        raceId = property.Value.GetString();
                    }
                }
                if (!foundRaceId || !IsCanonicalRaceId(raceId) || !seen.Add(raceId!)) return false;
                raceIds.Add(raceId!);
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool IsCanonicalRaceId(string? value) => value is not null
        && value.StartsWith("race-", StringComparison.Ordinal)
        && Guid.TryParseExact(value[5..], "D", out var id)
        && string.Equals(value, $"race-{id:D}", StringComparison.Ordinal);

    private static SqliteCommand CreateInCommand(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<string> keys, string commandTemplate)
    {
        var names = keys.Select((_, index) => "$id" + index).ToArray();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = commandTemplate.Replace("{ids}", string.Join(",", names), StringComparison.Ordinal);
        for (var index = 0; index < keys.Count; index++) command.Parameters.AddWithValue(names[index], keys[index]);
        return command;
    }

    private static string? NormalizeSource(string? identity, string? url, out bool valid)
    {
        valid = true;
        string? identityValue = null;
        string? urlValue = null;
        if (!string.IsNullOrWhiteSpace(identity))
        {
            if (IdentityMigrationPreflightPolicy.IsCanonicalJraIdentity(identity)) identityValue = identity;
            else if (TryNormalizeUrl(identity, out var normalizedIdentity)) identityValue = normalizedIdentity;
            else valid = false;
        }
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (TryNormalizeUrl(url, out var normalizedUrl)) urlValue = normalizedUrl;
            else valid = false;
        }
        if (identityValue is not null && urlValue is not null
            && !string.Equals(identityValue, urlValue, StringComparison.Ordinal)) valid = false;
        return valid ? identityValue ?? urlValue : null;
    }

    private static bool TryNormalizeUrl(string value, out string? normalized)
    {
        normalized = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("www.jra.go.jp", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
            || !JraSourceIdentity.TryNormalizeHorse(value, out var result)
            || !IdentityMigrationPreflightPolicy.IsCanonicalJraIdentity(result)) return false;
        normalized = result;
        return true;
    }

    private static bool TryValidateMetadata(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json,
                new JsonDocumentOptions { MaxDepth = IdentityMigrationPreflightPolicy.MaximumJsonDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name)) return false;
            }
            return GetString(document.RootElement, "event_name") == nameof(HorseRegistered)
                && GetString(document.RootElement, "aggregate_name") == nameof(HorseAggregate)
                && TryGetVersion(document.RootElement, out var version) && version == 1;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryReadRegistration(string json, out string? registeredName, out string? normalizedName,
        out DateOnly? birthDate, out bool truncated)
    {
        registeredName = null;
        normalizedName = null;
        birthDate = null;
        truncated = false;
        try
        {
            using var document = JsonDocument.Parse(json,
                new JsonDocumentOptions { MaxDepth = IdentityMigrationPreflightPolicy.MaximumJsonDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "RegisteredName", "NormalizedName", "SexCode", "BirthDate", "OwnerName", "BreederName",
                "SireName", "DamName", "DamsireName", "CoatColor",
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name) || !expected.Contains(property.Name)) return false;
                if (property.Name.Equals("NormalizedName", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("BirthDate", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return false;
                }
                else if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return false;
            }
            if (seen.Count != expected.Count || expected.Any(name => !seen.Contains(name))) return false;
            registeredName = GetString(document.RootElement, "RegisteredName");
            normalizedName = GetString(document.RootElement, "NormalizedName");
            if (registeredName is null) return false;
            registeredName = SafeName(registeredName, out var registeredTooLong);
            var normalizedTooLong = false;
            if (normalizedName is not null) normalizedName = SafeName(normalizedName, out normalizedTooLong);
            if (registeredTooLong || normalizedTooLong)
            {
                truncated = true;
                registeredName = normalizedName = null;
                return false;
            }
            var birthText = GetString(document.RootElement, "BirthDate");
            if (birthText is not null)
            {
                if (!DateOnly.TryParseExact(birthText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed)) return false;
                birthDate = parsed;
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryGetVersion(JsonElement metadata, out int version)
    {
        version = 0;
        foreach (var property in metadata.EnumerateObject())
        {
            if (!property.Name.Equals("event_version", StringComparison.OrdinalIgnoreCase)) continue;
            if (property.Value.ValueKind == JsonValueKind.Number) return property.Value.TryGetInt32(out version);
            return property.Value.ValueKind == JsonValueKind.String
                && int.TryParse(property.Value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out version);
        }
        return false;
    }

    private static string? GetString(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        return null;
    }

    private static string? SafeName(string value, out bool invalid)
    {
        invalid = value.Length > MaxNameCharacters || value.Any(char.IsControl);
        return invalid ? null : value;
    }

    private static string? GetUnambiguousIdentity(string? first, string? second) =>
        first is not null && second is not null && !string.Equals(first, second, StringComparison.Ordinal)
            ? null : first ?? second;

    private static string GetDataSource(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.DataSource == ":memory:")
            throw new InvalidOperationException("File-backed EventStore is required for bounded preflight reads.");
        return Path.GetFullPath(builder.DataSource);
    }

    private sealed record CurrentHorse(IdentityMigrationPreflightObservationStatus Status,
        string? RegisteredName, string? NormalizedName, DateOnly? BirthDate);
    private sealed record CurrentProfile(string? Identity, IdentityMigrationPreflightObservationStatus Status);
    private sealed record Registration(IdentityMigrationPreflightObservationStatus Status,
        string? RegisteredName, string? NormalizedName, DateOnly? BirthDate);
    private sealed record RaceReferenceEvidence(
        IReadOnlyList<IdentityMigrationPreflightRaceReference> Races,
        int RaceCount,
        bool Truncated,
        IdentityMigrationPreflightObservationStatus Status);
    private sealed record RegistrationRow(string AggregateName, string? Data, long DataBytes,
        string? Metadata, long MetadataBytes);
    private sealed record KeyPage(List<string> Keys, bool HasMore, bool HasUnsafeKey, int OverlongCount);
}

internal sealed record IdentityMigrationPreflightScannedRows(
    int RowsInspected,
    int UnknownIdentifierCount,
    IReadOnlyList<IdentityMigrationPreflightHorseEvidence> Evidence,
    bool ContainsTruncation,
    string? LastKey,
    bool HasMore = false,
    bool NoSafeContinuation = false);
