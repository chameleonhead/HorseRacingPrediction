using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetTaskViewCountsEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/operations/task-view-counts",
            async (CollectionPlatformStore store, CancellationToken token) =>
            {
                var counts = await store.GetTaskViewCountsAsync(token);
                return Results.Ok(new GetTaskViewCountsResponse(CollectionContractMapper.ToDto(counts)));
            }).Produces<GetTaskViewCountsResponse>(StatusCodes.Status200OK);
}
