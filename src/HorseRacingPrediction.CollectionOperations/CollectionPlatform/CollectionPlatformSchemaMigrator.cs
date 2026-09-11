using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

internal static class CollectionPlatformSchemaMigrator
{
    internal const int CurrentVersion = 1;
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
            }
            else
            {
                var missing = ModelTables.Except(existing, StringComparer.Ordinal).ToArray();
                if (missing.Length != 0)
                    throw new InvalidOperationException(
                        "Existing collection platform database has an incomplete schema. Missing tables: "
                        + string.Join(", ", missing));
            }

            await ExecuteAsync(connection,
                $"INSERT INTO {HistoryTable} (version, applied_at) VALUES (1, $appliedAt);",
                cancellationToken, transaction, ("$appliedAt", (object)DateTimeOffset.UtcNow.ToString("O")))
                .ConfigureAwait(false);
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
