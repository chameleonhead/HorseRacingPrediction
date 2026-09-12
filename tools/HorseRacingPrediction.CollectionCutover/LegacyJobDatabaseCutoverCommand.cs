using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HorseRacingPrediction.CollectionCutover;

public sealed record LegacyJobDatabaseCutoverReport(bool DryRun, bool AlreadyRemoved,
    IReadOnlyList<string> Targets, string? BackupDirectory, Guid? SmokeTaskId);

public static class LegacyJobDatabaseCutoverCommand
{
    private const string LegacyFileName = "collection-tasks.db";
    private static readonly string[] AllowedNames = [LegacyFileName, LegacyFileName + "-wal", LegacyFileName + "-shm"];

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        var values = Parse(args);
        if (!values.TryGetValue("--state-dir", out var stateDirectory) || string.IsNullOrWhiteSpace(stateDirectory))
        {
            await error.WriteLineAsync("Usage: --state-dir <directory> [--execute --confirm-delete-legacy-job-db --smoke-task-id <guid>]");
            return 2;
        }
        var root = Path.GetFullPath(stateDirectory);
        EnsureSafeRoot(root);
        var targets = AllowedNames.Select(name => Path.GetFullPath(Path.Combine(root, name))).ToArray();
        foreach (var target in targets) EnsureDirectChild(root, target);
        var existing = targets.Where(File.Exists).ToArray();
        var execute = values.ContainsKey("--execute");
        if (!execute)
        {
            await WriteAsync(output, new(true, existing.Length == 0, existing, null, null));
            return 0;
        }
        if (!values.ContainsKey("--confirm-delete-legacy-job-db")
            || !values.TryGetValue("--smoke-task-id", out var smokeText)
            || !Guid.TryParse(smokeText, out var smokeTaskId))
        {
            await error.WriteLineAsync("Execute requires --confirm-delete-legacy-job-db and a valid --smoke-task-id.");
            return 2;
        }
        var newDatabase = Path.Combine(root, "collection-platform.db");
        if (!await IsSuccessfulSmokeTaskAsync(newDatabase, smokeTaskId, cancellationToken).ConfigureAwait(false))
        {
            await error.WriteLineAsync("The smoke task does not exist in collection-platform.db or is not Succeeded.");
            return 3;
        }
        if (existing.Length == 0)
        {
            await WriteAsync(output, new(false, true, [], null, smokeTaskId));
            return 0;
        }
        var backupDirectory = Path.Combine(root, "cutover-backups", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
        Directory.CreateDirectory(backupDirectory);
        foreach (var source in existing)
            File.Copy(source, Path.Combine(backupDirectory, Path.GetFileName(source)), overwrite: false);
        foreach (var source in existing) File.Delete(source);
        await WriteAsync(output, new(false, false, existing, backupDirectory, smokeTaskId));
        return 0;
    }

    private static Dictionary<string, string?> Parse(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
            result[args[index]] = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index] : null;
        return result;
    }

    private static void EnsureSafeRoot(string root)
    {
        // Directory.GetParent handles both Windows volume roots (C:\) and the Unix root (/).
        // Trimming '/' first turns the Unix root into an empty string and incorrectly rejects
        // every absolute Unix path because its path root also trims to empty.
        if (Directory.GetParent(root) is null)
            throw new InvalidOperationException("State directory must not be a filesystem root.");
    }

    private static void EnsureDirectChild(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)
            || relative.Contains(Path.DirectorySeparatorChar) || !AllowedNames.Contains(relative, StringComparer.Ordinal))
            throw new InvalidOperationException("Legacy database target escaped the configured state directory.");
    }

    private static async Task<bool> IsSuccessfulSmokeTaskAsync(string databasePath, Guid taskId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath)) return false;
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM collection_tasks WHERE lower(TaskId)=lower($taskId);";
        command.Parameters.AddWithValue("$taskId", taskId.ToString());
        return string.Equals(Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)),
            "Succeeded", StringComparison.Ordinal);
    }

    private static Task WriteAsync(TextWriter output, LegacyJobDatabaseCutoverReport report)
        => output.WriteLineAsync(JsonSerializer.Serialize(report,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}
