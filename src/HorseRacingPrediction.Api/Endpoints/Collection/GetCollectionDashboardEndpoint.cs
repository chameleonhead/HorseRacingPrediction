using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetCollectionDashboardEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/operations/dashboard",
            async (CollectionPlatformStore store, CancellationToken token) =>
            {
                var progressTask = store.GetProgressAsync(token);
                var notificationsTask = store.GetActionableFailureNotificationsAsync(JstTime.Now(), 10000, token);
                var backfillsTask = store.GetBackfillBatchesAsync(token);
                await Task.WhenAll(progressTask, notificationsTask, backfillsTask);
                return Results.Ok(new CollectionOperationsDashboard(await progressTask,
                    CollectionFailureGrouping.Build(await notificationsTask), await backfillsTask, JstTime.Now()));
            });
}
