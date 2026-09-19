using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed partial class CollectionPlatformStore
{
    public async Task<CollectionMonitoringSnapshot> GetMonitoringSnapshotAsync(
        DateTimeOffset cutoff,
        DateTimeOffset dispatchesFrom,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(maxRows, 1, 10_000);
        await using var db = CreateDbContext();
        var control = await db.Controls.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ControlId == "pipeline", cancellationToken)
            .ConfigureAwait(false);
        var pipeline = control is null
            ? new CollectionPipelineState(false, null, null)
            : new CollectionPipelineState(control.IsPaused, control.Reason, control.UpdatedAt);

        var activeRows = await (from task in db.Tasks.AsNoTracking()
                                join resource in db.Resources.AsNoTracking()
                                    on task.ResourcePk equals resource.ResourcePk
                                where task.CreatedAt <= cutoff
                                      && (task.Status == CollectionTaskStatus.Pending
                                          || task.Status == CollectionTaskStatus.Ready
                                          || task.Status == CollectionTaskStatus.Running
                                          || task.Status == CollectionTaskStatus.RetryWaiting
                                          || task.Status == CollectionTaskStatus.WaitingDiscovery)
                                orderby task.UpdatedAt, task.TaskId
                                select new { task, resource })
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dispatchRows = await (from outbox in db.DispatchOutbox.AsNoTracking()
                                  join task in db.Tasks.AsNoTracking() on outbox.TaskId equals task.TaskId
                                  where outbox.DispatchedAt != null
                                        && outbox.DispatchedAt <= cutoff
                                        && outbox.DispatchedAt >= dispatchesFrom
                                  orderby outbox.DispatchedAt descending, outbox.OutboxId
                                  select new { outbox, task })
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var truncated = activeRows.Count > limit || dispatchRows.Count > limit;
        var active = activeRows.Take(limit).Select(x => new CollectionMonitoringTaskSnapshot(
            x.task.TaskId,
            new ResourceKey(x.resource.Type, x.resource.Provider, x.resource.ResourceId),
            new CollectionDefinitionId(x.task.DefinitionId),
            x.task.Status,
            x.task.Lane,
            x.task.Priority,
            x.task.AvailableAt,
            x.task.CreatedAt,
            x.task.UpdatedAt,
            x.task.StartedAt,
            x.task.LeaseExpiresAt,
            x.task.AttemptCount)).ToArray();
        var dispatches = dispatchRows.Take(limit).Select(x => new CollectionMonitoringDispatchSnapshot(
            x.outbox.EnvelopeId ?? Guid.Empty,
            x.task.TaskId,
            new CollectionDefinitionId(x.task.DefinitionId),
            x.task.Lane,
            x.task.Priority,
            x.outbox.AvailableAt,
            x.outbox.CreatedAt,
            x.outbox.DispatchedAt!.Value)).ToArray();
        return new(cutoff, pipeline, active, dispatches, truncated);
    }

    public async Task<CollectionMonitoringBackup> CreateMonitoringBackupAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            var source = (SqliteConnection)db.Database.GetDbConnection();
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            var dataSource = new SqliteConnectionStringBuilder(source.ConnectionString).DataSource;
            var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataSource))!, "backups");
            Directory.CreateDirectory(directory);
            var id = $"collection-monitor-{now:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
            var path = Path.Combine(directory, $"{id}.db");
            await using var destination = new SqliteConnection($"Data Source={path};Pooling=False");
            await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
            source.BackupDatabase(destination);
            foreach (var stale in new DirectoryInfo(directory)
                         .EnumerateFiles("collection-monitor-*.db")
                         .OrderByDescending(x => x.CreationTimeUtc)
                         .Skip(20))
                stale.Delete();
            return new(id, Path.GetFileName(path), now);
        }
        finally
        {
            _gate.Release();
        }
    }
}
