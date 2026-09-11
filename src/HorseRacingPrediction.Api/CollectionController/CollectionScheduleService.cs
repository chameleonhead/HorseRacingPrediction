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
            var now = DateTimeOffset.UtcNow;
            foreach (var state in await store.GetDueStatesAsync(now, cancellationToken: stoppingToken).ConfigureAwait(false))
            {
                var schedule = _policy.Evaluate(state.Resource, state, now);
                if (!schedule.ShouldCollect) continue;
                try
                {
                    await store.RequestAsync(state.Resource, state.Definition, state.RequiredRevision,
                        CollectionReason.ScheduledRefresh, now, schedule.Lane, (int)schedule.Priority,
                        cancellationToken: stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to schedule due resource {Resource}/{Definition}",
                        state.Resource, state.Definition);
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
