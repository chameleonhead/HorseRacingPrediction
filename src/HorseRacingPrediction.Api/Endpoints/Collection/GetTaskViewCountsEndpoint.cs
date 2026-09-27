using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetTaskViewCountsEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/operations/task-view-counts",
            async (CollectionPlatformStore store, CancellationToken token) =>
                Results.Ok(await store.GetTaskViewCountsAsync(token)));
}
