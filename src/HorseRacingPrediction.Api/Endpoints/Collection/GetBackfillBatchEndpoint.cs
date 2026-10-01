using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetBackfillBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/backfill-batches/{id}",
            async ([AsParameters] GetBackfillBatchRequest request, CollectionPlatformStore store, CancellationToken token) =>
                await store.GetBackfillBatchAsync(request.Id, token) is { } batch
                    ? Results.Ok(new GetBackfillBatchResponse(CollectionContractMapper.ToDto(batch))) : Results.NotFound())
            .Produces<GetBackfillBatchResponse>(StatusCodes.Status200OK);
}
