using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetCollectionResourceStateEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/resources/{type}/{provider}/{resourceId}/definitions/{definition}/state",
            async (CollectionResourceType type, string provider, string resourceId, string definition,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                var state = await store.GetStateAsync(new(type, provider, resourceId), new(definition), token);
                return state is null ? Results.NotFound() : Results.Ok(state);
            });
}
