using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionPlanningScheduler : BackgroundService
{
    private readonly ProcessingStateStore _store;
    private readonly CollectionMaintenanceState _maintenance;

    public CollectionPlanningScheduler(ProcessingStateStore store, CollectionMaintenanceState maintenance)
    {
        _store = store;
        _maintenance = maintenance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_maintenance.IsActive)
            {
                var now = DateTimeOffset.UtcNow;
                await _store.ScheduleJobAsync(
                AgentJobType.CollectionPlanning,
                "JRA:collection-planning",
                "{}",
                now,
                priority: 250,
                cancellationToken: stoppingToken).ConfigureAwait(false);
            }
            await Task.Delay(TimeSpan.FromHours(3), stoppingToken).ConfigureAwait(false);
        }
    }
}
