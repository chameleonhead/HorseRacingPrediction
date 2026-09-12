using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

internal static class CollectionPlatformSchemaMigrator
{
    internal const int CurrentVersion = 6;
    private const string HistoryTable = "collection_schema_history";

    private static readonly string[] ModelTables =
    [
        "collection_resources", "collection_definitions", "collection_revisions",
        "collection_revision_impacts", "collection_states", "collection_requests",
        "collection_tasks", "collection_active_tasks", "collection_attempts",
        "resource_locations", "collection_task_outbox"
    ];

    public static void Migrate(CollectionPlatformDbContext db)
        => MigrateAsync(db).GetAwaiter().GetResult();

    internal static async Task MigrateAsync(CollectionPlatformDbContext db,
        CancellationToken cancellationToken = default)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, "PRAGMA busy_timeout=30000;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);

        await using var transaction = connection.BeginTransaction(deferred: false);
        db.Database.UseTransaction(transaction);
        await ExecuteAsync(connection, $"""
            CREATE TABLE IF NOT EXISTS {HistoryTable} (
                version INTEGER NOT NULL CONSTRAINT PK_collection_schema_history PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
            """, cancellationToken, transaction).ConfigureAwait(false);

        var version = await ReadVersionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (version > CurrentVersion)
            throw new InvalidOperationException(
                $"Collection platform schema version {version} is newer than supported version {CurrentVersion}.");

        if (version == 0)
        {
            var existing = await ReadExistingModelTablesAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            if (existing.Count == 0)
            {
                await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken)
                    .ConfigureAwait(false);
                await ExecuteAsync(connection,
                    $"INSERT INTO {HistoryTable} (version, applied_at) VALUES ($version, $appliedAt);",
                    cancellationToken, transaction, ("$version", (object)CurrentVersion),
                    ("$appliedAt", (object)DateTimeOffset.UtcNow.ToString("O"))).ConfigureAwait(false);
                version = CurrentVersion;
            }
            else
            {
                var missing = ModelTables.Except(existing, StringComparer.Ordinal).ToArray();
                if (missing.Length != 0)
                    throw new InvalidOperationException(
                        "Existing collection platform database has an incomplete schema. Missing tables: "
                        + string.Join(", ", missing));
                var hasResolution = existing.Contains("collection_failure_notifications")
                    && await HasColumnAsync(connection, transaction, "collection_failure_notifications",
                        "ResolutionStatus", cancellationToken).ConfigureAwait(false);
                var hasOutboxReservation = await HasColumnAsync(connection, transaction, "collection_task_outbox",
                    "ReservationToken", cancellationToken).ConfigureAwait(false);
                var hasAttemptCorrelation = await HasColumnAsync(connection, transaction, "collection_attempts",
                    "ExecutionBatchId", cancellationToken).ConfigureAwait(false);
                var baselineVersion = existing.Contains("collection_platform_controls")
                    && existing.Contains("collection_failure_notifications")
                    ? existing.Contains("collection_backfill_batches")
                        ? hasResolution ? hasOutboxReservation ? hasAttemptCorrelation ? CurrentVersion : 5 : 4 : 3 : 2
                    : 1;
                await ExecuteAsync(connection,
                    $"INSERT INTO {HistoryTable} (version, applied_at) VALUES ($version, $appliedAt);",
                    cancellationToken, transaction, ("$version", (object)baselineVersion),
                    ("$appliedAt", (object)DateTimeOffset.UtcNow.ToString("O")))
                    .ConfigureAwait(false);
                version = baselineVersion;
            }
        }

        if (version < 2)
        {
            await ExecuteAsync(connection, """
                ALTER TABLE collection_tasks ADD COLUMN CancellationRequestedAt TEXT NULL;
                CREATE TABLE collection_platform_controls (
                    ControlId TEXT NOT NULL CONSTRAINT PK_collection_platform_controls PRIMARY KEY,
                    IsPaused INTEGER NOT NULL,
                    Reason TEXT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE TABLE collection_failure_notifications (
                    NotificationId TEXT NOT NULL CONSTRAINT PK_collection_failure_notifications PRIMARY KEY,
                    TaskId TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    ErrorCode TEXT NULL,
                    ErrorMessage TEXT NULL,
                    AttemptCount INTEGER NOT NULL,
                    FailedAt TEXT NOT NULL,
                    AvailableAt TEXT NOT NULL,
                    PublishedAt TEXT NULL,
                    PublishAttemptCount INTEGER NOT NULL,
                    LastPublishError TEXT NULL
                );
                CREATE INDEX IX_collection_failure_notifications_PublishedAt_AvailableAt
                    ON collection_failure_notifications (PublishedAt, AvailableAt);
                CREATE INDEX IX_collection_failure_notifications_TaskId
                    ON collection_failure_notifications (TaskId);
                INSERT INTO collection_schema_history (version, applied_at) VALUES (2, $appliedAt);
                """, cancellationToken, transaction,
                ("$appliedAt", (object)DateTimeOffset.UtcNow.ToString("O"))).ConfigureAwait(false);
            version = 2;
        }

        if (version < 3)
        {
            await ExecuteAsync(connection, """
                CREATE TABLE collection_backfill_batches (
                    BatchId TEXT NOT NULL CONSTRAINT PK_collection_backfill_batches PRIMARY KEY,
                    Provider TEXT NOT NULL,
                    "From" TEXT NOT NULL,
                    "To" TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    ExpansionCompletedAt TEXT NULL
                );
                CREATE INDEX IX_collection_backfill_batches_Provider_From_To
                    ON collection_backfill_batches (Provider, "From", "To");
                INSERT INTO collection_schema_history (version, applied_at) VALUES (3, $appliedAt);
                """, cancellationToken, transaction,
                ("$appliedAt", (object)DateTimeOffset.UtcNow.ToString("O"))).ConfigureAwait(false);
            version = 3;
        }

        if (version < 4)
        {
            await ExecuteAsync(connection, """
                ALTER TABLE collection_failure_notifications ADD COLUMN ResolutionStatus TEXT NOT NULL DEFAULT 'Open';
                ALTER TABLE collection_failure_notifications ADD COLUMN RecoveryTaskId TEXT NULL;
                ALTER TABLE collection_failure_notifications ADD COLUMN RecoveryStartedAt TEXT NULL;
                ALTER TABLE collection_failure_notifications ADD COLUMN ResolvedAt TEXT NULL;
                CREATE INDEX IX_collection_failure_notifications_ResolutionStatus_AvailableAt
                    ON collection_failure_notifications (ResolutionStatus, AvailableAt);
                CREATE INDEX IX_collection_failure_notifications_RecoveryTaskId
                    ON collection_failure_notifications (RecoveryTaskId);
                INSERT INTO collection_schema_history (version, applied_at) VALUES (4, $appliedAt);
                """, cancellationToken, transaction,
                ("$appliedAt", (object)DateTimeOffset.UtcNow.ToString("O"))).ConfigureAwait(false);
            version = 4;
        }

        if (version < 5)
        {
            await ExecuteAsync(connection, """
                ALTER TABLE collection_task_outbox ADD COLUMN ReservationToken TEXT NULL;
                ALTER TABLE collection_task_outbox ADD COLUMN ReservedUntilUnixMilliseconds INTEGER NULL;
                ALTER TABLE collection_task_outbox ADD COLUMN EnvelopeId TEXT NULL;
                ALTER TABLE collection_task_outbox ADD COLUMN QueueMessageId TEXT NULL;
                CREATE INDEX IX_collection_task_outbox_DispatchedAt_ReservedUntilUnixMilliseconds
                    ON collection_task_outbox (DispatchedAt, ReservedUntilUnixMilliseconds);
                INSERT INTO collection_schema_history (version, applied_at) VALUES (5, $appliedAt);
                """, cancellationToken, transaction,
                ("$appliedAt", (object)DateTimeOffset.UtcNow.ToString("O"))).ConfigureAwait(false);
            version = 5;
        }

        if (version < 6)
        {
            await ExecuteAsync(connection, """
                ALTER TABLE collection_attempts ADD COLUMN ExecutionBatchId TEXT NULL;
                ALTER TABLE collection_attempts ADD COLUMN DispatchEnvelopeId TEXT NULL;
                ALTER TABLE collection_attempts ADD COLUMN QueueMessageId TEXT NULL;
                ALTER TABLE collection_attempts ADD COLUMN LambdaRequestId TEXT NULL;
                ALTER TABLE collection_attempts ADD COLUMN BatchTaskOrdinal INTEGER NULL;
                ALTER TABLE collection_attempts ADD COLUMN BatchTaskCount INTEGER NULL;
                CREATE INDEX IX_collection_attempts_ExecutionBatchId
                    ON collection_attempts (ExecutionBatchId);
                INSERT INTO collection_schema_history (version, applied_at) VALUES (6, $appliedAt);
                """, cancellationToken, transaction,
                ("$appliedAt", (object)DateTimeOffset.UtcNow.ToString("O"))).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        db.Database.UseTransaction(null);
        await ExecuteAsync(connection, "PRAGMA quick_check;", cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadVersionAsync(SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COALESCE(MAX(version), 0) FROM {HistoryTable};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<HashSet<string>> ReadExistingModelTablesAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE 'collection_%' " +
                              "OR type = 'table' AND name = 'resource_locations';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            if (!string.Equals(name, HistoryTable, StringComparison.Ordinal)) tables.Add(name);
        }
        return tables;
    }

    private static async Task<bool> HasColumnAsync(SqliteConnection connection, SqliteTransaction transaction,
        string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql,
        CancellationToken cancellationToken, SqliteTransaction? transaction = null,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
