using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetCollectionResourceStateEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/resources/{type}/{provider}/{resourceId}/definitions/{definition}/state",
            async ([AsParameters] GetCollectionResourceStateRequest request,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                var state = await store.GetStateAsync(new(request.Type, request.Provider, request.ResourceId),
                    new(request.DefinitionId), token);
                return state is null ? Results.NotFound()
                    : Results.Ok(new GetCollectionResourceStateResponse(CollectionContractMapper.ToDto(state)));
            }).Produces<GetCollectionResourceStateResponse>(StatusCodes.Status200OK);
}
