using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetExecutionBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/execution-batches/{id:guid}",
            async (Guid id, CollectionPlatformStore store, CancellationToken token) =>
                await store.GetExecutionBatchAsync(id, token) is { } batch
                    ? Results.Ok(batch) : Results.NotFound());
}
