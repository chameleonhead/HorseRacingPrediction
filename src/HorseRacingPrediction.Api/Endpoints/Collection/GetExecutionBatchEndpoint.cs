using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetExecutionBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/execution-batches/{id:guid}",
            async ([AsParameters] GetExecutionBatchRequest request, CollectionPlatformStore store, CancellationToken token) =>
                await store.GetExecutionBatchAsync(request.Id, token) is { } batch
                    ? Results.Ok(new GetExecutionBatchResponse(CollectionContractMapper.ToDto(batch))) : Results.NotFound())
            .Produces<GetExecutionBatchResponse>(StatusCodes.Status200OK);
}
