using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetCollectionPipelineEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/pipeline-state",
            async (CollectionPlatformStore store, CancellationToken token) =>
                Results.Ok(await store.GetPipelineStateAsync(token)));
}
