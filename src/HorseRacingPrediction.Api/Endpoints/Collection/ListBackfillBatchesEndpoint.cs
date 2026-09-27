using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class ListBackfillBatchesEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/backfill-batches",
            async (CollectionPlatformStore store, CancellationToken token) =>
                Results.Ok(await store.GetBackfillBatchesAsync(token)));
}
