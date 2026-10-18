using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using EventFlow.Aggregates;
using EventFlow.EventStores;
using HorseRacingPrediction.Api.Identity;
using HorseRacingPrediction.Contracts.Identity;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Memos;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.Api.IdentityMigration;

internal sealed record HorseIdentityMigrationPostVerificationResult(
    int ComparedDatabaseCount,
    long ComparedRows,
    IReadOnlyList<HorseIdentityMigrationReplayTarget> ExpectedReplayTargets);

/// <summary>Compares every configured application SQLite store with the approved backup's typed expected state.</summary>
internal sealed class HorseIdentityMigrationPostVerification(
    HorseIdentityMigrationManagedBackup managedBackup,
    IEventJsonSerializer eventJsonSerializer,
    IDomainEventFactory domainEventFactory)
{
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(30);
    private const long MaximumSnapshotFileBytes = HorseIdentityMigrationManagedBackup.MaximumPayloadBytes;
    private const int MaximumSchemaObjects = 512;
    private const int MaximumSchemaSqlBytes = 8192;
    private const int MaximumSchemaIdentifierBytes = HorseIdentityMigrationSchemaDiagnostics.MaximumIdentifierBytes;
    private const int MaximumSchemaColumns = 512;
    private const int MaximumSchemaFacetBytes = 8192;
    private const int MaximumInternalFieldBytes = 64 * 1024;

    internal async Task<HorseIdentityMigrationPostVerificationResult> VerifyAsync(
        Guid operationId,
        HorseIdentityMigrationConfiguredSources sources,
        HorseIdentityMigrationJournal journal,
        IReadOnlyList<HorseIdentityMigrationMapping> mappings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(eventJsonSerializer);
        ArgumentNullException.ThrowIfNull(domainEventFactory);
        if (operationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(operationId));
        if (mappings.Count == 0) throw new InvalidDataException("A nonempty validated identity mapping is required.");

        using var timeout = new CancellationTokenSource(MaximumDuration);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = operation.Token;
        var stopwatch = Stopwatch.StartNew();
        var backupPaths = await managedBackup.GetApplicationSourcesAsync(operationId, sources, journal, token)
            .ConfigureAwait(false);
        var actualPaths = GetConfiguredApplicationSources(sources);
        ValidateClosedSourceSet(backupPaths, actualPaths, sources);
        var allPaths = backupPaths.Values.Concat(actualPaths.Values).ToArray();
        foreach (var path in allPaths)
        {
            token.ThrowIfCancellationRequested();
            HorseIdentityMigrationRuntimeOwnership.ValidateExistingAncestors(path, sourceFile: true);
            EnsureNoDataBearingSidecars(path);
        }
        var before = await SnapshotFilesAsync(allPaths, token).ConfigureAwait(false);

        var reader = new TypedHorseIdentityReferenceReader(eventJsonSerializer, domainEventFactory);
        var backupBudget = new TypedHorseIdentityExecutionBudget();
        var actualBudget = new TypedHorseIdentityExecutionBudget();
        var replayBudget = new TypedHorseIdentityExecutionBudget();
        var expectedReplayTargets = new Dictionary<(HorseIdentityMigrationReplayAggregateKind Kind, string Id), int>();
        long comparedRows = 0;

        foreach (var surface in Enum.GetValues<MigrationStoreSurface>())
        {
            token.ThrowIfCancellationRequested();
            if (!backupPaths.TryGetValue(surface, out var backupPath)) continue;
            if (!actualPaths.TryGetValue(surface, out var actualPath))
                throw new InvalidDataException("A configured application database is absent from the post-check set.");

            await using var backupConnection = new SqliteConnection(
                HorseIdentityMigrationReadonlyReplay.CreateImmutableReadonlyConnectionString(backupPath));
            await using var actualConnection = new SqliteConnection(
                HorseIdentityMigrationReadonlyReplay.CreateImmutableReadonlyConnectionString(actualPath));
            await backupConnection.OpenAsync(token).ConfigureAwait(false);
            await actualConnection.OpenAsync(token).ConfigureAwait(false);
            await EnsureIntegrityAsync(backupConnection, token).ConfigureAwait(false);
            await EnsureIntegrityAsync(actualConnection, token).ConfigureAwait(false);

            var backupSchema = await ReadSchemaSignatureAsync(backupConnection, token).ConfigureAwait(false);
            var actualSchema = await ReadSchemaSignatureAsync(actualConnection, token).ConfigureAwait(false);
            if (!SchemaSignaturesEqual(backupSchema, actualSchema))
                throw new InvalidDataException($"The {surface} database schema differs from its approved backup.");

            var backupPages = await reader.PrepareCanonicalExecutionPagesAsync(surface, backupConnection,
                mappings, backupBudget, token).ConfigureAwait(false);
            var actualPages = await reader.PrepareCanonicalExecutionPagesAsync(surface, actualConnection,
                mappings, actualBudget, token).ConfigureAwait(false);
            ComparePages(surface, backupPages, actualPages, ref comparedRows);
            await CompareInternalTablesAsync(backupConnection, actualConnection, backupSchema, backupBudget,
                actualBudget, token).ConfigureAwait(false);

            if (surface == MigrationStoreSurface.EventStore)
                await CollectExpectedReplayTargetsAsync(reader, backupConnection, backupPages, mappings,
                    expectedReplayTargets, replayBudget, token).ConfigureAwait(false);
            EnsureWithinBounds(stopwatch, token);
        }

        if (backupPaths.Count != actualPaths.Count)
            throw new InvalidDataException("The configured application database set changed during verification.");

        var replayTargets = expectedReplayTargets
            .OrderBy(pair => pair.Key.Kind)
            .ThenBy(pair => pair.Key.Id, StringComparer.Ordinal)
            .Select(pair => new HorseIdentityMigrationReplayTarget(pair.Key.Kind, pair.Key.Id, pair.Value))
            .ToArray();
        await HorseIdentityMigrationReadonlyReplay.VerifyAsync(
            actualPaths[MigrationStoreSurface.EventStore], replayTargets, token).ConfigureAwait(false);

        var after = await SnapshotFilesAsync(allPaths, token).ConfigureAwait(false);
        if (!SnapshotsEqual(before, after))
            throw new InvalidDataException("Readonly post-verification changed a database or SQLite companion file.");
        EnsureWithinBounds(stopwatch, token);
        return new(backupPaths.Count, comparedRows, replayTargets);
    }

    private static Dictionary<MigrationStoreSurface, string> GetConfiguredApplicationSources(
        HorseIdentityMigrationConfiguredSources sources)
    {
        var paths = new Dictionary<MigrationStoreSurface, string>();
        Add(MigrationStoreSurface.EventStore, sources.EventStorePath);
        Add(MigrationStoreSurface.CollectionPlatform, sources.CollectionPlatformPath);
        Add(MigrationStoreSurface.PredictionSchedule, sources.PredictionSchedulePath);
        if (sources.LocalCollectionQueuePath is not null)
            Add(MigrationStoreSurface.LocalCollectionQueue, sources.LocalCollectionQueuePath);
        if (paths.Values.Select(Path.GetFullPath).Distinct(HorseIdentityMigrationConfiguredSources.PathComparer).Count()
            != paths.Count)
            throw new InvalidDataException("Configured application stores cannot alias one another.");
        return paths;

        void Add(MigrationStoreSurface surface, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("A configured application database is unavailable.");
            paths.Add(surface, Path.GetFullPath(path));
        }
    }

    private static void ValidateClosedSourceSet(
        IReadOnlyDictionary<MigrationStoreSurface, string> backupPaths,
        IReadOnlyDictionary<MigrationStoreSurface, string> actualPaths,
        HorseIdentityMigrationConfiguredSources sources)
    {
        var expectedCount = sources.LocalCollectionQueuePath is null ? 3 : 4;
        if (backupPaths.Count != expectedCount || actualPaths.Count != expectedCount
            || backupPaths.Keys.Order().SequenceEqual(actualPaths.Keys.Order()) is false)
            throw new InvalidDataException("The configured application database membership is incomplete.");
        var backupSet = backupPaths.Values.Select(Path.GetFullPath)
            .ToHashSet(HorseIdentityMigrationConfiguredSources.PathComparer);
        if (actualPaths.Values.Any(path => backupSet.Contains(Path.GetFullPath(path))))
            throw new InvalidDataException("A backup database aliases a live application database.");
    }

    private static void EnsureNoDataBearingSidecars(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-journal" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar) && new FileInfo(sidecar).Length > 0)
                throw new InvalidDataException("Immutable verification cannot accept a nonempty WAL or rollback journal.");
        }
    }

    private static async Task EnsureIntegrityAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        command.CommandTimeout = 30;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.FieldCount != 1 || !string.Equals(reader.GetString(0), "ok", StringComparison.Ordinal))
                throw new InvalidDataException("A configured SQLite database failed integrity verification.");
            count++;
        }
        if (count != 1) throw new InvalidDataException("SQLite integrity verification returned an invalid result.");
    }

    private static async Task<IReadOnlyList<SchemaObjectSignature>> ReadSchemaSignatureAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new List<SchemaObjectSignature>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT type,name,tbl_name,sql,
                       length(CAST(name AS BLOB)),
                       length(CAST(tbl_name AS BLOB)),
                       coalesce(length(CAST(sql AS BLOB)),0)
                FROM sqlite_schema ORDER BY type,name,tbl_name LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", MaximumSchemaObjects + 1);
            command.CommandTimeout = 30;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Count >= MaximumSchemaObjects
                    || reader.GetInt64(4) > MaximumSchemaIdentifierBytes
                    || reader.GetInt64(5) > MaximumSchemaIdentifierBytes
                    || reader.GetInt64(6) > MaximumSchemaSqlBytes)
                    throw new InvalidDataException("SQLite schema inventory exceeds its established bounds.");
                result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), []));
            }
        }

        for (var index = 0; index < result.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = result[index];
            if (!string.Equals(item.Type, "table", StringComparison.Ordinal)) continue;
            if (item.Name.Contains('"'))
                throw new InvalidDataException("A configured SQLite table identifier is unsupported.");
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT cid,name,type,"notnull",dflt_value,pk,hidden,
                       length(CAST(name AS BLOB)),length(CAST(type AS BLOB)),
                       coalesce(length(CAST(dflt_value AS BLOB)),0)
                FROM pragma_table_xinfo($table) LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$table", item.Name);
            command.Parameters.AddWithValue("$limit", MaximumSchemaColumns + 1);
            command.CommandTimeout = 30;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var columns = new List<SchemaColumnSignature>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (columns.Count >= MaximumSchemaColumns
                    || reader.GetInt64(7) > MaximumSchemaIdentifierBytes
                    || reader.GetInt64(8) > MaximumSchemaIdentifierBytes
                    || reader.GetInt64(9) > MaximumSchemaFacetBytes)
                    throw new InvalidDataException("SQLite table column inventory exceeds its established bounds.");
                columns.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt32(3), reader.IsDBNull(4) ? null : SqlValue(reader.GetValue(4)),
                    reader.GetInt32(5), reader.GetInt32(6)));
            }
            result[index] = item with { Columns = columns };
        }
        return result;
    }

    private static async Task CompareInternalTablesAsync(
        SqliteConnection backupConnection,
        SqliteConnection actualConnection,
        IReadOnlyList<SchemaObjectSignature> schema,
        TypedHorseIdentityExecutionBudget backupBudget,
        TypedHorseIdentityExecutionBudget actualBudget,
        CancellationToken cancellationToken)
    {
        var internalTables = schema.Where(item => item.Type == "table"
                && item.Name.StartsWith("sqlite_", StringComparison.Ordinal))
            .Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
        foreach (var table in internalTables)
        {
            var expected = await ReadInternalTableAsync(backupConnection, table, backupBudget, cancellationToken)
                .ConfigureAwait(false);
            var actual = await ReadInternalTableAsync(actualConnection, table, actualBudget, cancellationToken)
                .ConfigureAwait(false);
            if (expected.RowCount != actual.RowCount
                || !string.Equals(expected.MembershipHash, actual.MembershipHash, StringComparison.Ordinal)
                || !string.Equals(expected.ValuesHash, actual.ValuesHash, StringComparison.Ordinal))
                throw new InvalidDataException($"Internal SQLite table {table} changed during identity migration.");
        }
    }

    private static bool SchemaSignaturesEqual(
        IReadOnlyList<SchemaObjectSignature> left,
        IReadOnlyList<SchemaObjectSignature> right)
    {
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            var expected = left[index];
            var actual = right[index];
            if (!string.Equals(expected.Type, actual.Type, StringComparison.Ordinal)
                || !string.Equals(expected.Name, actual.Name, StringComparison.Ordinal)
                || !string.Equals(expected.TableName, actual.TableName, StringComparison.Ordinal)
                || !string.Equals(expected.Sql, actual.Sql, StringComparison.Ordinal)
                || !expected.Columns.SequenceEqual(actual.Columns)) return false;
        }
        return true;
    }

    private static async Task<InternalTableSignature> ReadInternalTableAsync(
        SqliteConnection connection,
        string table,
        TypedHorseIdentityExecutionBudget budget,
        CancellationToken cancellationToken)
    {
        if (table.Contains('"')) throw new InvalidDataException("An internal SQLite table identifier is unsupported.");
        var columns = new List<string>();
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                SELECT name,length(CAST(name AS BLOB)),hidden
                FROM pragma_table_xinfo($table) LIMIT $limit;
                """;
            schema.Parameters.AddWithValue("$table", table);
            schema.Parameters.AddWithValue("$limit", MaximumSchemaColumns + 1);
            await using var reader = await schema.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.GetInt64(1) > MaximumSchemaIdentifierBytes || reader.GetInt32(2) == 0 && columns.Count >= MaximumSchemaColumns)
                    throw new InvalidDataException("Internal SQLite column inventory exceeds its established bounds.");
                if (reader.GetInt32(2) == 0) columns.Add(reader.GetString(0));
            }
        }
        if (columns.Count == 0 || columns.Any(column => column.Contains('"')))
            throw new InvalidDataException("An internal SQLite table has an unsupported schema.");

        long totalRows;
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = $"SELECT count(*) FROM {Quote(table)};";
            totalRows = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture);
        }
        var safeRows = 0L;
        using var membershipHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var valuesHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var command = connection.CreateCommand())
        {
            var safePredicate = string.Join(" AND ", columns.Select(column =>
                $"({Quote(column)} IS NULL OR length(CAST({Quote(column)} AS BLOB)) <= {MaximumInternalFieldBytes})"));
            command.CommandText = $"SELECT rowid,{string.Join(",", columns.Select(Quote))} FROM {Quote(table)} "
                + $"WHERE {safePredicate} ORDER BY rowid;";
            command.CommandTimeout = 30;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rowId = reader.GetInt64(0);
                long bytes = sizeof(long);
                AppendLong(membershipHash, rowId);
                AppendLong(valuesHash, rowId);
                for (var index = 0; index < columns.Count; index++)
                {
                    var value = reader.GetValue(index + 1);
                    if (value is string text) bytes += Encoding.UTF8.GetByteCount(text);
                    else if (value is byte[] blob) bytes += blob.Length;
                    else if (value is not DBNull) bytes += sizeof(long);
                    AppendString(valuesHash, columns[index]);
                    AppendString(valuesHash, SqlValue(value) ?? "<null>");
                }
                budget.AddRow(bytes, cancellationToken);
                safeRows++;
            }
        }
        if (safeRows != totalRows)
            throw new InvalidDataException("An internal SQLite value exceeded its established field bound.");
        return new(totalRows, Convert.ToHexString(membershipHash.GetHashAndReset()),
            Convert.ToHexString(valuesHash.GetHashAndReset()));
    }

    private static void AppendLong(IncrementalHash hash, long value) => AppendString(hash,
        value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void AppendString(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void ComparePages(
        MigrationStoreSurface surface,
        IReadOnlyList<TypedHorseIdentityExecutionPage> expected,
        IReadOnlyList<TypedHorseIdentityExecutionPage> actual,
        ref long comparedRows)
    {
        if (expected.Count != actual.Count)
            throw new InvalidDataException($"The {surface} table or row-page membership differs from its backup.");
        for (var index = 0; index < expected.Count; index++)
        {
            var expectedPage = expected[index];
            var actualPage = actual[index];
            var expectedEvidence = expectedPage.Evidence;
            var actualEvidence = actualPage.Evidence;
            if (expectedEvidence.Surface != actualEvidence.Surface
                || !string.Equals(expectedEvidence.Table, actualEvidence.Table, StringComparison.Ordinal)
                || expectedEvidence.RowCount != actualEvidence.RowCount
                || !string.Equals(expectedEvidence.FirstKey, actualEvidence.FirstKey, StringComparison.Ordinal)
                || !string.Equals(expectedEvidence.LastKey, actualEvidence.LastKey, StringComparison.Ordinal)
                || !string.Equals(expectedPage.MembershipHash, actualPage.MembershipHash, StringComparison.Ordinal)
                || !string.Equals(expectedEvidence.ExpectedAfterHash, actualEvidence.BeforeHash, StringComparison.Ordinal))
                throw new InvalidDataException($"The {surface} database does not match every typed expected backup row.");
            comparedRows = checked(comparedRows + expectedEvidence.RowCount);
        }
    }

    private static async Task CollectExpectedReplayTargetsAsync(
        TypedHorseIdentityReferenceReader reader,
        SqliteConnection backupConnection,
        IReadOnlyList<TypedHorseIdentityExecutionPage> eventPages,
        IReadOnlyList<HorseIdentityMigrationMapping> mappings,
        IDictionary<(HorseIdentityMigrationReplayAggregateKind Kind, string Id), int> targets,
        TypedHorseIdentityExecutionBudget budget,
        CancellationToken cancellationToken)
    {
        var aggregateNames = new Dictionary<string, HorseIdentityMigrationReplayAggregateKind>(StringComparer.Ordinal)
        {
            [new HorseAggregate(HorseId.New).Name.Value] = HorseIdentityMigrationReplayAggregateKind.Horse,
            [new RaceAggregate(RaceId.New).Name.Value] = HorseIdentityMigrationReplayAggregateKind.Race,
            [new MemoAggregate(MemoId.New).Name.Value] = HorseIdentityMigrationReplayAggregateKind.Memo,
            [new PredictionTicketAggregate(PredictionTicketId.New).Name.Value] =
                HorseIdentityMigrationReplayAggregateKind.PredictionTicket,
        };
        var columns = await ReadColumnNamesAsync(backupConnection, "EventEntity", cancellationToken).ConfigureAwait(false);
        var aggregateIdIndex = FindColumn(columns, "AggregateId");
        var aggregateNameIndex = FindColumn(columns, "AggregateName");
        var aggregateSequenceIndex = FindColumn(columns, "AggregateSequenceNumber");
        foreach (var page in eventPages.Where(page => page.Evidence.Table == "EventEntity"))
        {
            var rows = await reader.ReadAndTransformExecutionPageAsync(backupConnection, page.Evidence,
                mappings, cancellationToken, budget).ConfigureAwait(false);
            foreach (var row in rows)
            {
                var aggregateName = row.ExpectedValues[aggregateNameIndex] as string;
                if (aggregateName is null || !aggregateNames.TryGetValue(aggregateName, out var kind)) continue;
                if (row.ExpectedValues[aggregateIdIndex] is not string id || string.IsNullOrWhiteSpace(id))
                    throw new InvalidDataException("A backup aggregate root has an invalid identifier.");
                var sequence = Convert.ToInt32(row.ExpectedValues[aggregateSequenceIndex],
                    System.Globalization.CultureInfo.InvariantCulture);
                if (sequence <= 0) throw new InvalidDataException("A backup aggregate root has an invalid version.");
                var key = (kind, id);
                if (!targets.TryGetValue(key, out var current))
                {
                    budget.AddPage(Encoding.UTF8.GetByteCount(id));
                    targets.Add(key, sequence);
                }
                else if (sequence > current)
                {
                    targets[key] = sequence;
                }
            }
        }
    }

    private static async Task<IReadOnlyList<string>> ReadColumnNamesAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        if (table.Contains('"')) throw new InvalidDataException("A SQLite table identifier is unsupported.");
        var columns = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Quote(table)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) columns.Add(reader.GetString(1));
        if (columns.Count == 0) throw new InvalidDataException("The EventEntity table has no columns.");
        return columns;
    }

    private static int FindColumn(IReadOnlyList<string> columns, string expected)
    {
        for (var index = 0; index < columns.Count; index++)
            if (string.Equals(columns[index], expected, StringComparison.Ordinal)) return index;
        throw new InvalidDataException($"The EventEntity table is missing {expected}.");
    }

    private static async Task<Dictionary<string, FileSignature>> SnapshotFilesAsync(
        IEnumerable<string> databasePaths,
        CancellationToken cancellationToken)
    {
        var snapshots = new Dictionary<string, FileSignature>(HorseIdentityMigrationConfiguredSources.PathComparer);
        foreach (var databasePath in databasePaths)
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm", databasePath + "-journal" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (snapshots.ContainsKey(path)) continue;
                snapshots.Add(path, await SnapshotFileAsync(path, cancellationToken).ConfigureAwait(false));
            }
        return snapshots;
    }

    private static async Task<FileSignature> SnapshotFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new(false, 0, string.Empty);
        var info = new FileInfo(path);
        if (info.Length < 0 || info.Length > MaximumSnapshotFileBytes)
            throw new InvalidDataException("A database or SQLite companion exceeds its verification bound.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long readBytes = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            readBytes = checked(readBytes + read);
            if (readBytes > info.Length || readBytes > MaximumSnapshotFileBytes)
                throw new InvalidDataException("A database or SQLite companion grew during readonly verification.");
            hash.AppendData(buffer, 0, read);
        }
        if (readBytes != info.Length) throw new InvalidDataException("A database or SQLite companion changed during hashing.");
        return new(true, readBytes, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static bool SnapshotsEqual(
        IReadOnlyDictionary<string, FileSignature> before,
        IReadOnlyDictionary<string, FileSignature> after) => before.Count == after.Count
        && before.All(pair => after.TryGetValue(pair.Key, out var signature) && pair.Value == signature);

    private static void EnsureWithinBounds(Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stopwatch.Elapsed >= MaximumDuration)
            throw new InvalidDataException("Readonly post-verification exceeded its duration bound.");
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string? SqlValue(object value) => value switch
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

    private sealed record SchemaObjectSignature(
        string Type,
        string Name,
        string TableName,
        string? Sql,
        IReadOnlyList<SchemaColumnSignature> Columns);

    private sealed record SchemaColumnSignature(
        int ColumnId,
        string Name,
        string Type,
        int NotNull,
        string? DefaultValue,
        int PrimaryKeyPosition,
        int Hidden);

    private sealed record InternalTableSignature(long RowCount, string MembershipHash, string ValuesHash);
    private sealed record FileSignature(bool Exists, long Length, string Sha256);
}
