using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed partial class ProcessingStateStore
{
    public async Task<string> RequestRaceDayReacquisitionAsync(
        RaceDayReacquisitionPayload payload,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = CreateDbContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var prefix = $"{payload.ProviderType}:{payload.RaceDate:yyyy-MM-dd}:";
            var active = await db.Jobs.FirstOrDefaultAsync(
                x => x.JobType == AgentJobType.RaceDayReacquisition
                    && x.DeduplicationKey.StartsWith(prefix)
                    && (x.IsHeld || x.Status == AgentJobStatus.Pending || x.Status == AgentJobStatus.Ready
                        || x.Status == AgentJobStatus.Running || x.Status == AgentJobStatus.WaitingDependency),
                cancellationToken).ConfigureAwait(false);
            if (active is not null) return active.JobId;

            var key = prefix + Guid.NewGuid().ToString("N");
            var job = new ProcessingJobEntity
            {
                JobId = BuildJobId(AgentJobType.RaceDayReacquisition, key),
                JobType = AgentJobType.RaceDayReacquisition,
                DeduplicationKey = key,
                Payload = AgentJobPayloadSerializer.Serialize(payload),
                Status = AgentJobStatus.Ready,
                Priority = 240,
                FirstQueuedAt = now,
                AvailableAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Jobs.Add(job);
            db.JobOperationAudits.Add(new JobOperationAuditEntity
            {
                AuditId = Guid.NewGuid().ToString("N"),
                JobId = job.JobId,
                Operation = "ReacquireRaceDay",
                PreviousStatus = AgentJobStatus.Pending,
                NewStatus = AgentJobStatus.Ready,
                ActorId = actor,
                Reason = string.IsNullOrWhiteSpace(payload.Reason)
                    ? $"開催日単位の再取得: {payload.RaceDate:yyyy-MM-dd}"
                    : payload.Reason,
                CreatedAt = now,
            });
            QueueDispatch(db, job, now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return job.JobId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentJobDetailReadModel?> GetRaceDayReacquisitionAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        var prefix = $"JRA:{raceDate:yyyy-MM-dd}:";
        var jobs = await db.Jobs.AsNoTracking()
            .Where(x => x.JobType == AgentJobType.RaceDayReacquisition && x.DeduplicationKey.StartsWith(prefix))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var latest = jobs.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        return latest is null ? null : await GetJobDetailAsync(latest.JobId, cancellationToken).ConfigureAwait(false);
    }
}
