using Microsoft.EntityFrameworkCore;
namespace HorseRacingPrediction.Collector.Scheduling;

public sealed partial class ProcessingStateStore
{
    public async Task<string> RequestSubjectCollectionAsync(string jobType, SubjectCollectionPayload payload,
        string actor, DateTimeOffset now, CancellationToken token = default)
    {
        if (jobType is not (AgentJobType.SubjectProfileRefresh or AgentJobType.HorseHistoryDiscovery))
            throw new ArgumentException("収集操作が不正です。");
        await _gate.WaitAsync(token);
        try
        {
            await using var db = CreateDbContext();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var prefix = payload.SubjectId + ":";
            var active = await db.Jobs.FirstOrDefaultAsync(x => x.JobType == jobType && x.DeduplicationKey.StartsWith(prefix)
                && (x.Status == AgentJobStatus.Pending || x.Status == AgentJobStatus.Ready || x.Status == AgentJobStatus.Running || x.Status == AgentJobStatus.WaitingDependency), token);
            if (active is not null) return active.JobId;
            var key = prefix + Guid.NewGuid().ToString("N");
            var job = new ProcessingJobEntity
            {
                JobId = BuildJobId(jobType, key),
                JobType = jobType,
                DeduplicationKey = key,
                Payload = AgentJobPayloadSerializer.Serialize(payload),
                Status = AgentJobStatus.Ready,
                Priority = 100,
                FirstQueuedAt = now,
                AvailableAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Jobs.Add(job);
            db.JobOperationAudits.Add(new JobOperationAuditEntity
            {
                AuditId = Guid.NewGuid().ToString("N"),
                JobId = job.JobId,
                Operation = "RequestSubjectCollection",
                PreviousStatus = AgentJobStatus.Pending,
                NewStatus = AgentJobStatus.Ready,
                ActorId = actor,
                Reason = payload.Name + " / " + jobType,
                CreatedAt = now
            });
            QueueDispatch(db, job, now);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return job.JobId;
        }
        finally { _gate.Release(); }
    }

    public async Task<SubjectCollectionStatus?> GetSubjectCollectionAsync(string jobType, string subjectId, CancellationToken token = default)
    {
        await using var db = CreateDbContext();
        var prefix = subjectId + ":";
        var jobs = await db.Jobs.AsNoTracking().Where(x => x.JobType == jobType && x.DeduplicationKey.StartsWith(prefix)).ToListAsync(token);
        var latest = jobs.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        if (latest is null) return null;
        var detail = (await GetJobDetailAsync(latest.JobId, token))!;
        var children = await db.Jobs.AsNoTracking().Where(x => x.ParentJobId == latest.JobId).ToListAsync(token);
        var excluded = children.Where(x => x.JobType == AgentJobType.HorseHistoryExcluded).ToArray();
        var races = children.Where(x => x.JobType == AgentJobType.HorseHistoryRace).ToArray();
        return new(detail, races.Length, races.Count(x => x.Status == AgentJobStatus.Succeeded),
            races.Count(x => x.Status is AgentJobStatus.Failed or AgentJobStatus.DeadLetter), excluded.Length,
            excluded.Select(x => AgentJobPayloadSerializer.Deserialize<HorseHistoryRacePayload>(x.Payload))
                .Select(x => $"{x.RaceDate:yyyy/MM/dd} {x.Course} {x.RaceName}: {x.ExclusionReason}").ToArray());
    }
}
