using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed partial class ProcessingStateStore
{
    public async Task<RaceMutationLeaseDecision> ValidateRaceMutationLeaseAsync(
        string? raceId, DateOnly? raceDate, string? jobId, string? leaseToken,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = new ProcessingStateDbContext(_dbContextOptions);
            var now = DateTimeOffset.UtcNow;
            var running = await db.Jobs.AsNoTracking()
                .Where(x => x.Status == AgentJobStatus.Running && !x.IsHeld)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var candidates = running.Where(x => x.LeaseToken is not null && x.LeaseExpiresAt > now
                && (x.JobType == AgentJobType.RaceReacquisition || x.JobType == AgentJobType.RaceDayReacquisition));

            var active = candidates.FirstOrDefault(x => IsTarget(x, raceId, raceDate));
            if (active is null)
                return new RaceMutationLeaseDecision(true, false, null, null);

            var allowed = string.Equals(active.JobId, jobId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(leaseToken)
                && string.Equals(active.LeaseToken, leaseToken, StringComparison.Ordinal);
            return new RaceMutationLeaseDecision(allowed, true, active.JobId, active.LeaseExpiresAt);
        }
        finally { _gate.Release(); }
    }

    private static bool IsTarget(ProcessingJobEntity job, string? raceId, DateOnly? raceDate)
    {
        try
        {
            if (job.JobType == AgentJobType.RaceReacquisition)
                return !string.IsNullOrWhiteSpace(raceId)
                    && string.Equals(AgentJobPayloadSerializer.Deserialize<RaceReacquisitionPayload>(job.Payload).RaceId, raceId, StringComparison.Ordinal);
            if (job.JobType == AgentJobType.RaceDayReacquisition && raceDate.HasValue)
                return AgentJobPayloadSerializer.Deserialize<RaceDayReacquisitionPayload>(job.Payload).RaceDate == raceDate.Value;
        }
        catch { return false; }
        return false;
    }
}
