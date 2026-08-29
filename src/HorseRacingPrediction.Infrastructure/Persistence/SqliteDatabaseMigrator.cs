using EventFlow.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Infrastructure.Persistence;

public sealed class SqliteDatabaseMigrator
{
    private const string InitialMigrationName = "InitialEventStore";

    private static readonly HashSet<string> InitialTables =
    [
        "EventEntity",
        "HorseRaceHistories",
        "Horses",
        "HorseWeightHistories",
        "JockeyRaceHistories",
        "Jockeys",
        "MemoSubjects",
        "PredictionComparisons",
        "PredictionTickets",
        "RacePredictionContexts",
        "RaceResults",
        "RaceSummaries",
        "SnapshotEntity",
        "Trainers"
    ];

    private static readonly HashSet<string> CurrentEnsureCreatedTables =
    [
        .. InitialTables,
        "OwnerAliasMappings",
        "OwnerMergeAudits"
    ];

    private readonly IDbContextProvider<EventStoreDbContext> _contextProvider;
    private readonly SqliteMigrationOptions _options;
    private readonly ILogger<SqliteDatabaseMigrator> _logger;

    public SqliteDatabaseMigrator(
        IDbContextProvider<EventStoreDbContext> contextProvider,
        IOptions<SqliteMigrationOptions> options,
        ILogger<SqliteDatabaseMigrator> logger)
    {
        _contextProvider = contextProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _contextProvider.CreateContext();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        await ExecutePragmaAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "PRAGMA busy_timeout=30000;", cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "PRAGMA synchronous=NORMAL;", cancellationToken).ConfigureAwait(false);
        await VerifyIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);

        var allMigrations = context.Database.GetMigrations().ToList();
        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var pendingMigrations = allMigrations.Where(migration => !appliedMigrations.Contains(migration)).ToList();

        if (pendingMigrations.Count == 0)
        {
            _logger.LogInformation("SQLite schema is current.");
            return;
        }

        if (_options.BackupBeforeMigration)
            await BackupAsync(connection, cancellationToken).ConfigureAwait(false);

        if (appliedMigrations.Count == 0)
            await BaselineExistingDatabaseAsync(connection, allMigrations, cancellationToken).ConfigureAwait(false);

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await VerifyIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Applied SQLite migrations: {Migrations}", string.Join(", ", pendingMigrations));
    }

    private static async Task BaselineExistingDatabaseAsync(
        SqliteConnection connection,
        IReadOnlyCollection<string> migrations,
        CancellationToken cancellationToken)
    {
        var existingTables = await GetUserTablesAsync(connection, cancellationToken).ConfigureAwait(false);
        if (existingTables.Count == 0)
            return;

        var isInitialSchema = existingTables.SetEquals(InitialTables);
        var isCurrentEnsureCreatedSchema = existingTables.SetEquals(CurrentEnsureCreatedTables);
        if (!isInitialSchema && !isCurrentEnsureCreatedSchema)
        {
            var missing = InitialTables.Except(existingTables).OrderBy(x => x);
            var unexpected = existingTables.Except(InitialTables).OrderBy(x => x);
            throw new InvalidOperationException(
                $"既存SQLite DBをベースライン化できません。Missing=[{string.Join(", ", missing)}], " +
                $"Unexpected=[{string.Join(", ", unexpected)}]");
        }

        var initialMigration = migrations.SingleOrDefault(migration =>
            migration.EndsWith("_" + InitialMigrationName, StringComparison.Ordinal));
        if (initialMigration is null)
            throw new InvalidOperationException("InitialEventStore migrationが見つかりません。");

        var baselineMigrations = isCurrentEnsureCreatedSchema ? migrations : [initialMigration];
        foreach (var migration in baselineMigrations)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ($migrationId, $productVersion);
            """;
            command.Parameters.AddWithValue("$migrationId", migration);
            command.Parameters.AddWithValue("$productVersion", "8.0.11");
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task BackupAsync(SqliteConnection source, CancellationToken cancellationToken)
    {
        var dataSource = new SqliteConnectionStringBuilder(source.ConnectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            return;

        var databasePath = Path.GetFullPath(dataSource);
        var backupDirectory = string.IsNullOrWhiteSpace(_options.BackupDirectory)
            ? Path.Combine(Path.GetDirectoryName(databasePath)!, "backups")
            : Path.GetFullPath(_options.BackupDirectory);
        Directory.CreateDirectory(backupDirectory);

        var backupPath = Path.Combine(
            backupDirectory,
            $"{Path.GetFileNameWithoutExtension(databasePath)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.db");
        await using var destination = new SqliteConnection($"Data Source={backupPath}");
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        source.BackupDatabase(destination);
        _logger.LogInformation("Created SQLite migration backup at {BackupPath}", backupPath);

        foreach (var obsoleteBackup in Directory
                     .EnumerateFiles(backupDirectory, $"{Path.GetFileNameWithoutExtension(databasePath)}-*.db")
                     .OrderByDescending(File.GetCreationTimeUtc)
                     .Skip(Math.Max(1, _options.BackupRetentionCount)))
        {
            File.Delete(obsoleteBackup);
        }
    }

    private static async Task<HashSet<string>> GetUserTablesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
              AND name <> '__EFMigrationsHistory';
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(reader.GetString(0));
        return result;
    }

    private static async Task VerifyIntegrityAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SQLite integrity check failed: {result}");
    }

    private static async Task ExecutePragmaAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureOpenAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }
}
