using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HorseRacingPrediction.Api.CollectionController;

internal static class CollectionBackgroundSchedulerRegistration
{
    internal static IServiceCollection AddCollectionBackgroundSchedulers(
        this IServiceCollection services,
        bool enabled)
    {
        if (!enabled) return services;

        services.AddHostedService<CollectionScheduleService>();
        services.AddHostedService<CollectionBackfillRecoveryService>();
        services.AddHostedService<CollectionPlanningScheduler>();
        return services;
    }
}
