using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionPlanningScheduler : BackgroundService
{
    private readonly ProcessingStateStore _store;
    private readonly CollectionMaintenanceState _maintenance;
    private readonly AgentProcessingOptions _options;

    public CollectionPlanningScheduler(ProcessingStateStore store, CollectionMaintenanceState maintenance, IOptions<AgentProcessingOptions> options)
    {
        _store = store;
        _maintenance = maintenance;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_maintenance.IsActive && !(await _store.GetCollectionPipelineStateAsync(stoppingToken)).IsPaused)
            {
                var now = DateTimeOffset.UtcNow;
                var planningBucketHour = now.Hour / 3 * 3;
                var planningBucket = new DateTimeOffset(now.Year, now.Month, now.Day, planningBucketHour, 0, 0, TimeSpan.Zero);
                await _store.EnqueueJobAsync(
                    AgentJobType.CollectionPlanning,
                    $"JRA:collection-planning:{planningBucket:yyyyMMddHH}",
                    "{}",
                    now,
                    priority: 250,
                    cancellationToken: stoppingToken).ConfigureAwait(false);

                if (_options.EnableAutonomousHistoricalCollection)
                {
                    var interval = Math.Max(1, _options.AcquisitionPlanReviewIntervalMinutes);
                    var bucket = new DateTimeOffset(
                        now.Year, now.Month, now.Day, now.Hour, now.Minute / interval * interval, 0, TimeSpan.Zero);
                    await _store.EnqueueJobAsync(
                        AgentJobType.AcquisitionPlanReview,
                        $"JRA:acquisition-plan-review:{bucket:yyyyMMddHHmm}",
                        AgentJobPayloadSerializer.Serialize(new AcquisitionPlanReviewPayload(bucket)),
                        now,
                        priority: 245,
                        cancellationToken: stoppingToken).ConfigureAwait(false);
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
