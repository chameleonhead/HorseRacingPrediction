using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.CollectionController;

internal static class CollectionBackgroundSchedulerRegistration
{
    internal static IServiceCollection AddCollectionBackgroundSchedulers(
        this IServiceCollection services,
        bool enabled,
        bool watchdogEnabled = true,
        int watchdogIntervalMinutes = 5,
        bool queueEnabled = false,
        int dispatchIntervalSeconds = 1,
        bool deadLetterReconcilerEnabled = false,
        int deadLetterIntervalSeconds = 30,
        bool metricDeliveryEnabled = false,
        bool alertsEnabled = true,
        bool maintenanceMode = false)
    {
        enabled &= !maintenanceMode;
        watchdogEnabled &= !maintenanceMode;
        queueEnabled &= !maintenanceMode;
        deadLetterReconcilerEnabled &= !maintenanceMode;
        metricDeliveryEnabled &= !maintenanceMode;
        alertsEnabled &= !maintenanceMode;
        var cadence = CollectionProducerCadencePolicy.GetWatchdogCadence(
            enabled, watchdogEnabled, watchdogIntervalMinutes);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<CollectionRuntimeStatusRecorder>(provider => new(
            CreateRuntimeActionConfigurations(enabled, cadence, queueEnabled, dispatchIntervalSeconds,
                deadLetterReconcilerEnabled, deadLetterIntervalSeconds, metricDeliveryEnabled, alertsEnabled),
            provider.GetRequiredService<TimeProvider>()));
        if (enabled)
        {
            services.AddSingleton<CollectionScheduleService>(provider => new(
                provider.GetRequiredService<CollectionPlatformStore>(),
                provider.GetServices<ICollectionSchedulePolicy>(),
                provider.GetRequiredService<ILogger<CollectionScheduleService>>(),
                provider.GetRequiredService<CollectionRuntimeStatusRecorder>()));
            services.AddSingleton<CollectionPlanningScheduler>(provider => new(
                provider.GetRequiredService<CollectionPlatformStore>(),
                provider.GetRequiredService<CollectionRuntimeStatusRecorder>()));
            services.AddSingleton<CollectionMaintenanceCoordinator>(provider => new(
                provider.GetRequiredService<CollectionPlatformStore>(),
                provider.GetRequiredService<CollectionScheduleService>(),
                provider.GetRequiredService<CollectionPlanningScheduler>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<CollectionMaintenanceCoordinator>>(),
                provider.GetRequiredService<CollectionRuntimeStatusRecorder>()));
            services.AddHostedService<CollectionMaintenanceCoordinator>(provider =>
                provider.GetRequiredService<CollectionMaintenanceCoordinator>());
            services.AddHostedService<CollectionBackfillRecoveryService>(provider =>
                new CollectionBackfillRecoveryService(
                    provider.GetRequiredService<CollectionPlatformStore>(),
                    provider.GetRequiredService<ILogger<CollectionBackfillRecoveryService>>(),
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<CollectionRuntimeStatusRecorder>()));
        }
        if (cadence.Enabled)
        {
            services.Configure<CollectionJobWatchdogOptions>(options =>
            {
                options.Enabled = true;
                options.IntervalMinutes = cadence.IntervalMinutes;
            });
            services.AddHostedService(provider => new CollectionPlatformWatchdogService(
                provider.GetRequiredService<CollectionPlatformStore>(),
                provider.GetRequiredService<IOptions<CollectionJobWatchdogOptions>>(),
                provider.GetRequiredService<ILogger<CollectionPlatformWatchdogService>>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<CollectionRuntimeStatusRecorder>()));
        }
        return services;
    }

    internal static IReadOnlyList<CollectionRuntimeActionConfiguration> CreateRuntimeActionConfigurations(
        bool backgroundSchedulersEnabled,
        WatchdogCadence watchdog,
        bool queueEnabled,
        int dispatchIntervalSeconds,
        bool deadLetterReconcilerEnabled,
        int deadLetterIntervalSeconds,
        bool metricDeliveryEnabled,
        bool alertsEnabled) =>
    [
        new(CollectionRuntimeAction.Dispatcher, queueEnabled,
            queueEnabled ? TimeSpan.FromSeconds(Math.Max(1, dispatchIntervalSeconds)) : null),
        new(CollectionRuntimeAction.DiscoveryPlanner, backgroundSchedulersEnabled,
            backgroundSchedulersEnabled ? TimeSpan.FromMinutes(1) : null),
        new(CollectionRuntimeAction.RefreshPlanner, backgroundSchedulersEnabled,
            backgroundSchedulersEnabled ? TimeSpan.FromMinutes(1) : null),
        new(CollectionRuntimeAction.TaskLeaseRecovery, watchdog.Enabled,
            watchdog.Enabled ? TimeSpan.FromMinutes(watchdog.IntervalMinutes) : null),
        new(CollectionRuntimeAction.BackfillRecovery, backgroundSchedulersEnabled,
            backgroundSchedulersEnabled ? TimeSpan.FromMinutes(5) : null),
        new(CollectionRuntimeAction.DeadLetterReconciliation, queueEnabled && deadLetterReconcilerEnabled,
            queueEnabled && deadLetterReconcilerEnabled
                ? TimeSpan.FromSeconds(Math.Max(1, deadLetterIntervalSeconds)) : null),
        new(CollectionRuntimeAction.Alerts, alertsEnabled,
            alertsEnabled ? TimeSpan.FromSeconds(5) : null),
        new(CollectionRuntimeAction.MetricDelivery, metricDeliveryEnabled, null),
    ];
}
