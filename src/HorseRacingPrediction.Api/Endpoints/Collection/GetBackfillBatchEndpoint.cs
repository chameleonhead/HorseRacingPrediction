using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetBackfillBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/backfill-batches/{id}",
            async (string id, CollectionPlatformStore store, CancellationToken token) =>
                await store.GetBackfillBatchAsync(id, token) is { } batch
                    ? Results.Ok(batch) : Results.NotFound());
}
