using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CreateCollectionRevisionEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/definitions/{definition}/revisions", async (string definition,
            CreateCollectionRevisionRequest request, CollectionPlatformStore store,
            IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
        {
            if (request.Revision is null)
                return Results.BadRequest(new { message = "Revision input is required." });
            var input = request.Revision;
            var affected = await store.AddRevisionAndApplyImpactAsync(new(definition), input.Revision,
                input.Description, CollectionPlatformEndpointSupport.BuildImpact(
                    CollectionContractMapper.ToInternal(input.Impact)), conditions, JstTime.Now(), token);
            return Results.Ok(new CreateCollectionRevisionResponse(
                new CollectionRevisionApplyResultDto(definition, input.Revision, affected)));
        }).Produces<CreateCollectionRevisionResponse>(StatusCodes.Status200OK);
}
