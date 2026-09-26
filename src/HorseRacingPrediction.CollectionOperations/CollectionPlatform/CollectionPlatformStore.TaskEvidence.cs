using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public sealed record CollectionTaskEvidence(CollectionTaskSummary Task, CollectionRequestSummary Request);

public sealed partial class CollectionPlatformStore
{
    /// <summary>Exact original task/request evidence; never substitute mutable resource metadata.</summary>
    public async Task<CollectionTaskEvidence?> GetTaskEvidenceAsync(Guid taskId, CancellationToken token = default)
    {
        await using var db = CreateDbContext();
        var row = await (from task in db.Tasks.AsNoTracking()
                         join resource in db.Resources.AsNoTracking() on task.ResourcePk equals resource.ResourcePk
                         join request in db.Requests.AsNoTracking() on task.RequestId equals request.RequestId
                         where task.TaskId == taskId
                         select new { task, resource, request }).SingleOrDefaultAsync(token);
        if (row is null) return null;
        return new(new(row.task.TaskId, new(row.resource.Type, row.resource.Provider, row.resource.ResourceId),
            new(row.task.DefinitionId), row.task.Status, row.task.Lane, row.task.Priority, row.task.RequestedRevision,
            row.task.AvailableAt, row.task.AttemptCount,
            row.task.MetadataJson is null ? null : DeserializeTaskMetadata(row.task.MetadataJson)),
            new(row.request.RequestId, row.request.RequestedRevision, row.request.Reason,
                row.request.RequestedAt, row.request.ExplicitUrl, row.request.BatchId));
    }
}
