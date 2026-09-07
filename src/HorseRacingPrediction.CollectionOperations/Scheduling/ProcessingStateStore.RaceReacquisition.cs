using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed partial class ProcessingStateStore
{
    public async Task<string> RequestRaceReacquisitionAsync(RaceReacquisitionPayload payload, string actor,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = CreateDbContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var prefix = payload.RaceId + ":";
            var active = await db.Jobs.FirstOrDefaultAsync(x => x.JobType == AgentJobType.RaceReacquisition
                && x.DeduplicationKey.StartsWith(prefix)
                && (x.Status == AgentJobStatus.Pending || x.Status == AgentJobStatus.Ready
                    || x.Status == AgentJobStatus.Running || x.Status == AgentJobStatus.WaitingDependency), cancellationToken);
            if (active is not null) return active.JobId;

            var key = prefix + Guid.NewGuid().ToString("N");
            var job = new ProcessingJobEntity
            {
                JobId = BuildJobId(AgentJobType.RaceReacquisition, key),
                JobType = AgentJobType.RaceReacquisition, DeduplicationKey = key,
                Payload = AgentJobPayloadSerializer.Serialize(payload), Status = AgentJobStatus.Ready,
                Priority = 100, FirstQueuedAt = now, AvailableAt = now, CreatedAt = now, UpdatedAt = now
            };
            db.Jobs.Add(job);
            db.JobOperationAudits.Add(new JobOperationAuditEntity
            {
                AuditId = Guid.NewGuid().ToString("N"), JobId = job.JobId, Operation = "ReacquireRace",
                PreviousStatus = AgentJobStatus.Pending, NewStatus = AgentJobStatus.Ready,
                ActorId = actor, Reason = "レース詳細から再取得: " + payload.RaceId, CreatedAt = now
            });
            QueueDispatch(db, job, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return job.JobId;
        }
        finally { _gate.Release(); }
    }

    public async Task<AgentJobDetailReadModel?> GetRaceReacquisitionAsync(string raceId, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var prefix = raceId + ":";
        var jobs = await db.Jobs.AsNoTracking().Where(x => x.JobType == AgentJobType.RaceReacquisition
            && x.DeduplicationKey.StartsWith(prefix)).ToListAsync(cancellationToken);
        var latest = jobs.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        return latest is null ? null : await GetJobDetailAsync(latest.JobId, cancellationToken);
    }
}
