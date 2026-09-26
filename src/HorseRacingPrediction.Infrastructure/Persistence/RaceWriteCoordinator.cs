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
