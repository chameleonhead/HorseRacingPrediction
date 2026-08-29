using System.Text.Json;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed record CollectionResetStatus(
    string State,
    string? Operation,
    string? Message,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? BackupDirectory,
    string? ActorId,
    string? Reason);

public sealed class CollectionResetCoordinator
{
    private readonly CollectionMaintenanceState _maintenance;
    private readonly ProcessingStateStore _store;
    private readonly ICollectionTaskQueue _queue;
    private readonly IDbContextProvider<EventStoreDbContext> _eventStoreProvider;
    private readonly SqliteDatabaseMigrator _migrator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CollectionResetCoordinator> _logger;
    private readonly object _statusGate = new();
    private CollectionResetStatus _status = new("Idle", null, null, null, null, null, null, null);

    public CollectionResetCoordinator(
        CollectionMaintenanceState maintenance,
        ProcessingStateStore store,
        ICollectionTaskQueue queue,
        IDbContextProvider<EventStoreDbContext> eventStoreProvider,
        SqliteDatabaseMigrator migrator,
        IConfiguration configuration,
        ILogger<CollectionResetCoordinator> logger)
    {
        _maintenance = maintenance;
        _store = store;
        _queue = queue;
        _eventStoreProvider = eventStoreProvider;
        _migrator = migrator;
        _configuration = configuration;
        _logger = logger;
        _status = LoadStatus() ?? _status;
    }

    public CollectionResetStatus GetStatus()
    {
        lock (_statusGate) return _status;
    }

    public async Task<IReadOnlyDictionary<string, long>> GetEventStoreTableCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _eventStoreProvider.CreateContext();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var names = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) names.Add(reader.GetString(0));
        }
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            await using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM \"{name.Replace("\"", "\"\"")}\";";
            result[name] = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }
        return result;
    }

    public bool TryStartQueueReset(string actorId, string reason)
        => TryStart("QueueReset", actorId, reason, token => RunQueueResetAsync(actorId, reason, token));

    public bool TryStartFullReset(string actorId, string reason, string reauthenticationPassword)
    {
        var expected = _configuration["ApiKey:Key"] ?? Environment.GetEnvironmentVariable("HORSE_RACING_API_KEY");
        if (string.IsNullOrEmpty(expected) || !string.Equals(expected, reauthenticationPassword, StringComparison.Ordinal))
            return false;
        return TryStart("FullReset", actorId, reason, token => RunFullResetAsync(actorId, reason, token));
    }

    public void ResumeIfNeeded()
    {
        var current = GetStatus();
        if (current.State != "Running" || !_maintenance.TryBegin()) return;
        var actor = current.ActorId ?? "recovery";
        var reason = current.Reason ?? "Api restart recovery";
        _ = RunInBackgroundAsync(current.Operation ?? "QueueReset",
            current.Operation == "FullReset"
                ? token => RunFullResetAsync(actor, reason, token)
                : token => RunQueueResetAsync(actor, reason, token));
    }

    private bool TryStart(string operation, string actorId, string reason, Func<CancellationToken, Task> action)
    {
        if (!_maintenance.TryBegin()) return false;
        SetStatus(new("Running", operation, "初期化を開始しました。", DateTimeOffset.UtcNow, null, null, actorId, reason));
        _ = RunInBackgroundAsync(operation, action);
        return true;
    }

    private Task RunInBackgroundAsync(string operation, Func<CancellationToken, Task> action)
        => Task.Run(async () =>
        {
            try { await action(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Collection reset failed. Operation={Operation}", operation);
                var current = GetStatus();
                SetStatus(current with { State = "Failed", Message = ex.Message, CompletedAt = DateTimeOffset.UtcNow });
            }
            finally { _maintenance.End(); }
        });

    private async Task RunQueueResetAsync(string actorId, string reason, CancellationToken cancellationToken, bool markCompleted = true)
    {
        SetMessage("タスクと古い投入世代を取り消しています。");
        await _store.CancelAllActiveJobsAsync(actorId, reason, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        SetMessage("SQS と DLQ を消去しています。");
        await _queue.PurgeAsync(cancellationToken).ConfigureAwait(false);
        var seconds = Math.Max(0, _configuration.GetValue("CollectionReset:StabilizationSeconds", 60));
        if (seconds > 0)
        {
            SetMessage($"SQS の安定化を {seconds} 秒待っています。");
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
        }
        if (markCompleted) Complete("キューを初期化しました。", null);
    }

    private async Task RunFullResetAsync(string actorId, string reason, CancellationToken cancellationToken)
    {
        await RunQueueResetAsync(actorId, reason, cancellationToken, markCompleted: false).ConfigureAwait(false);
        var backupRoot = ResolveBackupRoot();
        var backupDirectory = GetStatus().BackupDirectory
            ?? Path.Combine(backupRoot, $"full-reset-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}");
        Directory.CreateDirectory(backupDirectory);
        SetBackupDirectory(backupDirectory);
        SetMessage("Event Store をバックアップしています。");
        var eventStoreBackup = Path.Combine(backupDirectory, EventStoreFileName());
        if (!File.Exists(eventStoreBackup))
            eventStoreBackup = await BackupEventStoreAsync(backupDirectory, cancellationToken).ConfigureAwait(false);
        SetMessage("収集タスク DB をバックアップしています。");
        var taskStoreBackup = Path.Combine(backupDirectory, _configuration["CollectionProcessing:JobStoreFileName"] ?? "collection-tasks.db");
        if (!File.Exists(taskStoreBackup))
            taskStoreBackup = await _store.BackupDatabaseAsync(backupDirectory, cancellationToken).ConfigureAwait(false);
        SetMessage("Event Store を再作成しています。");
        await ResetEventStoreAsync(cancellationToken).ConfigureAwait(false);
        SetMessage("収集タスク DB を再作成しています。");
        await _store.ResetDatabaseAsync(cancellationToken).ConfigureAwait(false);

        var manifest = new
        {
            operation = "FullReset",
            actorId,
            reason,
            completedAt = DateTimeOffset.UtcNow,
            eventStoreBackup,
            taskStoreBackup
        };
        var manifestPath = Path.Combine(backupDirectory, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken)
            .ConfigureAwait(false);
        Complete("収集データベースを完全初期化しました。", backupDirectory);
    }

    private async Task<string> BackupEventStoreAsync(string backupDirectory, CancellationToken cancellationToken)
    {
        await using var context = _eventStoreProvider.CreateContext();
        var source = (SqliteConnection)context.Database.GetDbConnection();
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        var sourcePath = Path.GetFullPath(new SqliteConnectionStringBuilder(source.ConnectionString).DataSource);
        EnsureSafeEventStorePath(sourcePath);
        var backupPath = Path.Combine(backupDirectory, Path.GetFileName(sourcePath));
        await using (var destination = new SqliteConnection($"Data Source={backupPath};Pooling=False"))
        {
            await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
            source.BackupDatabase(destination);
            await VerifyAsync(source, cancellationToken).ConfigureAwait(false);
            await VerifyAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        return backupPath;
    }

    private async Task ResetEventStoreAsync(CancellationToken cancellationToken)
    {
        await using var context = _eventStoreProvider.CreateContext();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        var sourcePath = Path.GetFullPath(new SqliteConnectionStringBuilder(connection.ConnectionString).DataSource);
        EnsureSafeEventStorePath(sourcePath);
        await connection.CloseAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        await context.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
        await _migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    private string ResolveBackupRoot()
    {
        var configured = _configuration["DatabaseMigration:BackupDirectory"];
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var connectionString = _configuration.GetConnectionString("EventStore") ?? "Data Source=eventstore.db";
        var path = Path.GetFullPath(new SqliteConnectionStringBuilder(connectionString).DataSource);
        return Path.Combine(Path.GetDirectoryName(path)!, "backups");
    }

    private string EventStoreFileName()
    {
        var connectionString = _configuration.GetConnectionString("EventStore") ?? "Data Source=eventstore.db";
        return Path.GetFileName(new SqliteConnectionStringBuilder(connectionString).DataSource);
    }

    private void EnsureSafeEventStorePath(string sourcePath)
    {
        var dataDirectory = Directory.GetParent(ResolveBackupRoot())?.FullName
            ?? throw new InvalidOperationException("Could not resolve the data directory.");
        var root = Path.GetPathRoot(dataDirectory);
        if (string.Equals(dataDirectory.TrimEnd(Path.DirectorySeparatorChar), root?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The configured data directory is too broad for a destructive reset.");
        var relative = Path.GetRelativePath(dataDirectory, sourcePath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("The Event Store is outside the configured data directory.");
    }

    private static async Task VerifyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SQLite integrity check failed: {result}");
    }

    private void SetMessage(string message)
    {
        lock (_statusGate)
        {
            _status = _status with { Message = message };
            PersistStatus();
        }
    }

    private void Complete(string message, string? backupDirectory)
    {
        lock (_statusGate)
        {
            _status = _status with
            {
                State = "Completed",
                Message = message,
                CompletedAt = DateTimeOffset.UtcNow,
                BackupDirectory = backupDirectory
            };
            PersistStatus();
        }
    }

    private void SetBackupDirectory(string backupDirectory)
    {
        lock (_statusGate)
        {
            _status = _status with { BackupDirectory = backupDirectory };
            PersistStatus();
        }
    }

    private void SetStatus(CollectionResetStatus status)
    {
        lock (_statusGate)
        {
            _status = status;
            PersistStatus();
        }
    }

    private string StatePath
    {
        get
        {
            var backupRoot = ResolveBackupRoot();
            return Path.Combine(Directory.GetParent(backupRoot)?.FullName ?? backupRoot, "reset-state.json");
        }
    }

    private CollectionResetStatus? LoadStatus()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<CollectionResetStatus>(File.ReadAllText(StatePath))
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load collection reset state.");
            return null;
        }
    }

    private void PersistStatus()
    {
        var path = StatePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_status, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, path, true);
    }
}
