using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EventFlow.EntityFramework;
using EventFlow.EventStores;
using EventFlow.Aggregates;
using HorseRacingPrediction.Domain.Races;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Infrastructure.Persistence;

/// <summary>Cross-process locks tied to the physical SQLite database, with durable repair barriers.</summary>
public sealed class RaceWriteCoordinator(IDbContextProvider<EventStoreDbContext> provider, IEventStore events, IAggregateStore aggregates)
{
    private readonly string _memoryDirectory = Path.Combine(Path.GetTempPath(), "hrp-race-locks", Guid.NewGuid().ToString("N"));

    // The caller holds race/subject locks. Each database is online-backed-up independently;
    // this package is not a whole-application point-in-time restore authorization.
    public async Task<string> BackupRepairAsync(string raceId, string operationId, string manifestJson,
        Func<string, Task> backupCollection, CancellationToken token, bool requireExisting = false)
    {
        if (!Guid.TryParse(operationId, out var operation)) throw new ArgumentException("Invalid operation id.");
        var directory = Path.Combine(DirectoryPath, "backups", operation.ToString("D"));
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (requireExisting && !File.Exists(manifestPath)) throw new InvalidOperationException("RepairBackupMissing");
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        if (File.Exists(manifestPath))
        {
            var saved = JsonSerializer.Deserialize<RepairBackupManifest>(await File.ReadAllTextAsync(manifestPath, token))
                ?? throw new InvalidOperationException("RepairBackupInvalid");
            if (saved.RaceId != raceId || saved.SourceManifest != manifestJson) throw new InvalidOperationException("RepairBackupMismatch");
            await VerifyBackupAsync(directory, saved, token);
            return Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(manifestPath, token)));
        }
        // Incomplete packages are never overwritten or accepted after a crash.
        if (Directory.EnumerateFileSystemEntries(directory).Any()) throw new InvalidOperationException("RepairBackupIncomplete");
        using (var db = provider.CreateContext())
        {
            await db.Database.OpenConnectionAsync(token);
            using var destination = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            { DataSource = Path.Combine(directory, "events.db"), Pooling = false }.ToString());
            await destination.OpenAsync(token);
            ((Microsoft.Data.Sqlite.SqliteConnection)db.Database.GetDbConnection()).BackupDatabase(destination);
        }
        await backupCollection(Path.Combine(directory, "collection.db"));
        var sidecar = PathFor(raceId, ".repair");
        if (File.Exists(sidecar)) File.Copy(sidecar, Path.Combine(directory, "race.repair"), overwrite: false);
        var hashes = new Dictionary<string, string>();
        foreach (var file in Directory.EnumerateFiles(directory))
            hashes.Add(Path.GetFileName(file), Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file, token))));
        var manifest = new RepairBackupManifest(raceId, manifestJson, hashes);
        await VerifyBackupAsync(directory, manifest, token);
        using (var stream = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, manifest);
            stream.Flush(flushToDisk: true);
        }
        return Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(manifestPath, token)));
    }

    private static async Task VerifyBackupAsync(string directory, RepairBackupManifest manifest, CancellationToken token)
    {
        if (!manifest.Hashes.ContainsKey("events.db") || !manifest.Hashes.ContainsKey("collection.db")
            || manifest.Hashes.Keys.Any(x => x is not ("events.db" or "collection.db" or "race.repair")))
            throw new InvalidOperationException("RepairBackupInvalid");
        foreach (var pair in manifest.Hashes)
        {
            var path = Path.Combine(directory, pair.Key);
            if (Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, token))) != pair.Value)
                throw new InvalidOperationException("RepairBackupHashMismatch");
            if (!pair.Key.EndsWith(".db", StringComparison.Ordinal)) continue;
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            { DataSource = path, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await connection.OpenAsync(token);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check";
            if (await command.ExecuteScalarAsync(token) is not string result || result != "ok")
                throw new InvalidOperationException("RepairBackupIntegrityFailed");
        }
    }

    private sealed record RepairBackupManifest(string RaceId, string SourceManifest, Dictionary<string, string> Hashes);

    public async Task<string> AssignmentFingerprintAsync(string raceId, CancellationToken token)
    {
        using var db = provider.CreateContext();
        var race = await db.RacePredictionContexts.AsNoTracking().SingleOrDefaultAsync(x => x.RaceId == raceId, token);
        var assignments = race?.Entries.OrderBy(x => x.HorseId, StringComparer.Ordinal)
            .Select(x => new { x.HorseNumber, x.HorseId, x.GateNumber, x.ParticipationStatus }).ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { raceId, assignments }))));
    }

    private string DirectoryPath
    {
        get
        {
            using var db = provider.CreateContext();
            if (!db.Database.IsSqlite()) throw new InvalidOperationException("Race repair requires shared SQLite storage.");
            var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(db.Database.GetConnectionString());
            if (connection.Mode == Microsoft.Data.Sqlite.SqliteOpenMode.Memory || connection.DataSource == ":memory:")
                return _memoryDirectory;
            if (!Path.IsPathFullyQualified(connection.DataSource))
                throw new InvalidOperationException("Race lock storage requires an absolute database path.");
            return connection.DataSource + ".race-locks";
        }
    }

    private string PathFor(string key, string suffix)
    {
        var directory = DirectoryPath;
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + suffix);
    }

    public async Task<IAsyncDisposable> AcquireAsync(IEnumerable<string> keys, CancellationToken token,
        TimeSpan? timeout = null)
    {
        var handles = new List<FileStream>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        try
        {
            foreach (var key in keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                var path = PathFor(key, ".lock");
                while (true)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    try
                    {
                        handles.Add(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                        break;
                    }
                    catch (IOException)
                    {
                        await Task.Delay(40, deadline.Token).ConfigureAwait(false);
                    }
                }
            }
            return new Handles(handles);
        }
        catch
        {
            foreach (var handle in handles) handle.Dispose();
            throw;
        }
    }

    public async Task<RaceRepairBarrier?> ReadBarrierAsync(string raceId, CancellationToken token = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(raceId, "^race-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")) return null;
        var path = PathFor(raceId, ".repair");
        var stream = await events.LoadEventsAsync<RaceAggregate, RaceId>(new RaceId(raceId), token);
        var repairs = stream.Select(x => x.GetAggregateEvent()).OfType<RaceEntryAssignmentsRepaired>().ToArray();
        if (!File.Exists(path))
            return repairs.Length == 0 ? null : new(repairs[0].OperationId, repairs[0].Fingerprint, false);
        // A truncated/corrupt marker must block rather than silently unlock the race.
        var barrier = JsonSerializer.Deserialize<RaceRepairBarrier>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Invalid persistent race repair barrier.");
        if (!barrier.Verified) return barrier;
        // Sidecar and SQLite backups can be restored independently: never trust the file alone.
        if (repairs.Length != 1 || repairs[0].OperationId != barrier.OperationId || repairs[0].Fingerprint != barrier.Fingerprint)
            return barrier with { Verified = false };
        var inspection = await new RaceEntryRepairInspector(provider, events, aggregates).InspectAsync(raceId, token);
        // Comparison ticket rows are independently created after repair; their assignment indexes are checked separately.
        return inspection.Blockers.Any(x => (x.StartsWith("ProjectionMismatch:", StringComparison.Ordinal)
                && x != "ProjectionMismatch:ComparisonProjection")
            || x.StartsWith("AggregateSnapshot", StringComparison.Ordinal) || x.Contains("VersionMismatch", StringComparison.Ordinal)
            || x.StartsWith("UnknownSchema", StringComparison.Ordinal)
            || x == "RaceChangedDuringInspection") ? barrier with { Verified = false } : barrier;
    }

    public void WriteBarrier(string raceId, RaceRepairBarrier barrier)
    {
        var path = PathFor(raceId, ".repair");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough);
        JsonSerializer.Serialize(stream, barrier);
        stream.Flush(flushToDisk: true);
    }

    private sealed class Handles(List<FileStream> handles) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            foreach (var handle in handles) handle.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed record RaceRepairBarrier(string OperationId, string Fingerprint, bool Verified);
