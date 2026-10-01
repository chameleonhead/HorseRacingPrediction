using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetRevisionRecollectionProgressEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet(
        "/api/v2/admin/collection/recollection-batches", async ([AsParameters] GetRevisionRecollectionProgressRequest request,
            CollectionPlatformStore store, IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
            Results.Ok(new GetRevisionRecollectionProgressResponse(CollectionContractMapper.ToDto(
                await store.GetRevisionRecollectionProgressAsync(new(request.Definition), request.Revision, conditions, token)))))
            .Produces<GetRevisionRecollectionProgressResponse>(StatusCodes.Status200OK);
}
