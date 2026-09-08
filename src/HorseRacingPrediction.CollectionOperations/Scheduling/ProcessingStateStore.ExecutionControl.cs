using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed partial class ProcessingStateStore
{
    private static Task<bool> IsPausedAsync(ProcessingStateDbContext db, CancellationToken token)
        => db.Markers.AnyAsync(x => x.MarkerType == CollectionControlKeys.MarkerType && x.MarkerKey == CollectionControlKeys.MarkerKey, token);

    public async Task<CollectionPipelineState> GetCollectionPipelineStateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        if (!await IsPausedAsync(db, cancellationToken)) return new(false);
        var reason = await db.Markers.FirstOrDefaultAsync(x => x.MarkerType == CollectionControlKeys.ReasonType, cancellationToken);
        return reason is null ? new(true) : JsonSerializer.Deserialize<CollectionPipelineState>(reason.MarkerKey)!;
    }

    private static async Task PauseAsync(ProcessingStateDbContext db, string reason, string? jobId, CancellationToken token)
    {
        if (await IsPausedAsync(db, token)) return;
        var now = DateTimeOffset.UtcNow;
        db.Markers.Add(new() { MarkerType = CollectionControlKeys.MarkerType, MarkerKey = CollectionControlKeys.MarkerKey, CreatedAt = now });
        db.Markers.Add(new()
        {
            MarkerType = CollectionControlKeys.ReasonType,
            MarkerKey = JsonSerializer.Serialize(new CollectionPipelineState(true, reason, jobId, now)),
            CreatedAt = now
        });
    }

    public async Task PauseCollectionAsync(string reason, string? jobId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = CreateDbContext();
            await PauseAsync(db, reason, jobId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<int> ResumeCollectionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = CreateDbContext();
            db.Markers.RemoveRange(await db.Markers.Where(x =>
                (x.MarkerType == CollectionControlKeys.MarkerType && x.MarkerKey == CollectionControlKeys.MarkerKey)
                || x.MarkerType == CollectionControlKeys.ReasonType).ToListAsync(cancellationToken));
            // A queued notification may have been consumed while the pipeline was stopped.
            var ready = (await db.Jobs.Where(x => x.Status == AgentJobStatus.Ready && !x.IsHeld).ToListAsync(cancellationToken))
                .Where(x => CollectionDispatchPolicy.IsDispatchable(x.JobType)).ToList();
            foreach (var job in ready) QueueDispatch(db, job, job.AvailableAt);
            await db.SaveChangesAsync(cancellationToken);
            return ready.Count;
        }
        finally { _gate.Release(); }
    }

    public async Task<ForceRequeueJobResult> SetJobHoldAsync(string jobId, bool hold, DateTimeOffset expectedUpdatedAt,
        string actorId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = CreateDbContext();
            var job = await db.Jobs.SingleOrDefaultAsync(x => x.JobId == jobId, cancellationToken);
            if (job is null) return ForceRequeueJobResult.NotFound;
            if (job.UpdatedAt != expectedUpdatedAt || job.IsHeld == hold || !CollectionDispatchPolicy.IsDispatchable(job.JobType))
                return ForceRequeueJobResult.Conflict;
            if (job.Status is not (AgentJobStatus.Pending or AgentJobStatus.Ready or AgentJobStatus.Running or AgentJobStatus.WaitingDependency))
                return ForceRequeueJobResult.Conflict;
            var now = DateTimeOffset.UtcNow;
            if (!hold && job.Status == AgentJobStatus.Running)
            {
                if (job.LeaseExpiresAt is null || job.LeaseExpiresAt > now) return ForceRequeueJobResult.Conflict;
                await FinishHoldAsync(db, job, now, cancellationToken);
            }
            job.IsHeld = hold;
            job.UpdatedAt = now;
            job.DispatchGeneration++;
            db.JobOperationAudits.Add(new()
            {
                AuditId = Guid.NewGuid().ToString("N"),
                JobId = jobId,
                Operation = hold ? "Hold" : "ReleaseHold",
                PreviousStatus = job.Status,
                NewStatus = job.Status,
                ActorId = actorId,
                Reason = hold ? "指定したジョブのみを中断・保留" : "個別保留の解除",
                CreatedAt = now
            });
            if (!hold)
            {
                if (job.Status == AgentJobStatus.Ready) QueueDispatch(db, job, job.AvailableAt);
                if (job.Status == AgentJobStatus.WaitingDependency)
                {
                    var child = await db.Jobs.FirstOrDefaultAsync(x => x.ParentJobId == job.JobId, cancellationToken);
                    if (child is not null) await ReconcileParentJobAsync(db, child, now, cancellationToken);
                }
            }
            await db.SaveChangesAsync(cancellationToken);
            return ForceRequeueJobResult.Requeued;
        }
        finally { _gate.Release(); }
    }

    public async Task<CollectionLeaseControl> GetCollectionLeaseControlAsync(string jobId, string leaseToken, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.JobId == jobId, cancellationToken);
        if (job is null || job.Status != AgentJobStatus.Running || job.LeaseToken != leaseToken || job.LeaseExpiresAt <= DateTimeOffset.UtcNow)
            return CollectionLeaseControl.LeaseLost;
        return job.IsHeld ? CollectionLeaseControl.Hold : CollectionLeaseControl.Continue;
    }

    private static async Task FinishHoldAsync(ProcessingStateDbContext db, ProcessingJobEntity job, DateTimeOffset now, CancellationToken token)
    {
        await RecordAttemptAsync(db, job, AgentJobStatus.Cancelled, "個別保留により中断しました。", now, token);
        job.Status = AgentJobStatus.Ready;
        job.LeaseToken = null;
        job.LeaseExpiresAt = null;
        job.UpdatedAt = now;
    }

    public async Task<bool> AcknowledgeCollectionHoldAsync(string jobId, string leaseToken, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = CreateDbContext();
            var job = await db.Jobs.SingleOrDefaultAsync(x => x.JobId == jobId, cancellationToken);
            if (job is null || !job.IsHeld || job.Status != AgentJobStatus.Running || job.LeaseToken != leaseToken) return false;
            await FinishHoldAsync(db, job, DateTimeOffset.UtcNow, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally { _gate.Release(); }
    }

    public Task<bool> FailAndPauseCollectionTaskAsync(string jobType, string deduplicationKey, string leaseToken,
        string error, CancellationToken cancellationToken = default)
        => UpdateLeasedCollectionTaskAsync(jobType, deduplicationKey, leaseToken, AgentJobStatus.Failed, null, error, cancellationToken, pausePipeline: true);

    public Task<bool> WaitForCollectionDependenciesAsync(string jobType, string deduplicationKey, string leaseToken, CancellationToken cancellationToken = default)
        => UpdateLeasedCollectionTaskAsync(jobType, deduplicationKey, leaseToken, AgentJobStatus.WaitingDependency, null, null, cancellationToken);
}
