using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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

        var raceRows = await (from task in db.Tasks.AsNoTracking()
                              join resource in db.Resources.AsNoTracking()
                                  on task.ResourcePk equals resource.ResourcePk
                              where task.CreatedAt <= cutoff && task.DefinitionId == "race-detail"
                              orderby task.UpdatedAt descending, task.TaskId
                              select new { task, resource })
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var flowTasks = await db.Tasks.AsNoTracking()
            .Where(x => x.CreatedAt <= cutoff && (x.CreatedAt >= dispatchesFrom || x.FinishedAt >= dispatchesFrom))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var truncated = activeRows.Count > limit || dispatchRows.Count > limit || raceRows.Count > limit;
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
        var latestRaceRows = raceRows.Take(limit)
            .GroupBy(x => x.resource.ResourcePk)
            .Select(x => x.OrderByDescending(y => y.task.UpdatedAt).First())
            .ToArray();
        var raceResourcePks = latestRaceRows.Select(x => x.resource.ResourcePk).ToArray();
        var artifactRows = await db.RaceArtifactStates.AsNoTracking()
            .Where(x => raceResourcePks.Contains(x.ResourcePk))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var artifactByResource = artifactRows.ToLookup(x => x.ResourcePk);
        var scheduleByResource = await db.RaceSchedulingEvidence.AsNoTracking()
            .Where(x => raceResourcePks.Contains(x.ResourcePk))
            .ToDictionaryAsync(x => x.ResourcePk, cancellationToken).ConfigureAwait(false);
        var races = latestRaceRows.Select(x =>
        {
            var attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(
                x.task.MetadataJson ?? x.resource.AttributesJson) ?? [];
            var artifacts = artifactByResource[x.resource.ResourcePk];
            return new CollectionRaceFreshnessSnapshot(
                x.task.TaskId,
                new ResourceKey(x.resource.Type, x.resource.Provider, x.resource.ResourceId),
                x.task.Status,
                x.task.UpdatedAt,
                scheduleByResource.GetValueOrDefault(x.resource.ResourcePk)?.OfficialStartAt
                    ?? ParseInstant(attributes.GetValueOrDefault("officialStartAt")),
                artifacts.FirstOrDefault(y => y.Artifact == RaceArtifactKind.Card)?.Status
                    ?? ParseArtifactStatus(attributes.GetValueOrDefault("cardArtifactStatus")),
                artifacts.FirstOrDefault(y => y.Artifact == RaceArtifactKind.Result)?.Status
                    ?? ParseArtifactStatus(attributes.GetValueOrDefault("resultArtifactStatus")));
        }).ToArray();
        var dispatchedTaskIds = dispatchRows.Select(x => x.task.TaskId).ToHashSet();
        var flows = flowTasks.GroupBy(x => new { x.DefinitionId, x.Lane })
            .Select(group => new CollectionDefinitionFlowSnapshot(
                new(group.Key.DefinitionId), group.Key.Lane,
                $"{group.Key.Lane}:{group.Key.DefinitionId}",
                group.Count(x => x.CreatedAt >= dispatchesFrom),
                group.Count(x => dispatchedTaskIds.Contains(x.TaskId)),
                group.Count(x => x.FinishedAt >= dispatchesFrom),
                group.Count(x => x.FinishedAt is null),
                group.Where(x => x.FinishedAt is null).Select(x => (DateTimeOffset?)x.AvailableAt).Min()))
            .OrderBy(x => x.Definition.Value, StringComparer.Ordinal).ThenBy(x => x.Lane).ToArray();
        return new(cutoff, pipeline, active, dispatches, truncated, races, flows);
    }

    private static DateTimeOffset? ParseInstant(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static RaceArtifactStatus ParseArtifactStatus(string? value) =>
        Enum.TryParse<RaceArtifactStatus>(value, true, out var parsed) ? parsed : RaceArtifactStatus.Unknown;

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
