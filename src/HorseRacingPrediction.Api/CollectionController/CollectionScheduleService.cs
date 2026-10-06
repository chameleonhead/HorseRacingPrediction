using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionScheduleService(CollectionPlatformStore store,
    IEnumerable<ICollectionSchedulePolicy> policies,
    ILogger<CollectionScheduleService> logger) : BackgroundService
{
    private readonly ICollectionSchedulePolicy _policy = policies.Single();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = HorseRacingPrediction.Contracts.Common.Time.JstTime.Now();
            await RunOnceAsync(now, stoppingToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reclaimed = await store.ReclaimExpiredLeasesAsync(now, cancellationToken).ConfigureAwait(false);
        if (reclaimed > 0) logger.LogWarning("Reclaimed {Count} expired collection leases.", reclaimed);
        foreach (var candidate in await store.GetDueScheduleCandidatesAsync(now,
                     cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            var state = candidate.State;
            var schedule = _policy.Evaluate(state.Resource, state, now);
            if (!schedule.ShouldCollect || candidate.HasActiveTask) continue;
            try
            {
                await store.RequestAsync(state.Resource, state.Definition, state.RequiredRevision,
                    CollectionReason.ScheduledRefresh, now, schedule.Lane, (int)schedule.Priority,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to schedule due resource {Resource}/{Definition}",
                    state.Resource, state.Definition);
            }
        }
    }
}
