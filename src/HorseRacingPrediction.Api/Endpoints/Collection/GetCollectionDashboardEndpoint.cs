using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;

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
                var dashboard = new CollectionOperationsDashboard(await progressTask,
                    CollectionFailureGrouping.Build(await notificationsTask), await backfillsTask, JstTime.Now());
                return Results.Ok(new GetCollectionDashboardResponse(new(
                    CollectionContractMapper.ToDto(dashboard.Progress),
                    dashboard.Failures.Select(CollectionContractMapper.ToDto).ToArray(),
                    dashboard.Backfills.Select(CollectionContractMapper.ToDto).ToArray(), dashboard.GeneratedAt)));
            }).Produces<GetCollectionDashboardResponse>(StatusCodes.Status200OK);
}
