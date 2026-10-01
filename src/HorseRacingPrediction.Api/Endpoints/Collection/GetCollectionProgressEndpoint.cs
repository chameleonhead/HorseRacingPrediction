using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetCollectionProgressEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/operations/progress",
            async (CollectionPlatformStore store, CancellationToken token) =>
                Results.Ok(new GetCollectionProgressResponse(CollectionContractMapper.ToDto(
                    await store.GetProgressAsync(token)))))
            .Produces<GetCollectionProgressResponse>(StatusCodes.Status200OK);
}
