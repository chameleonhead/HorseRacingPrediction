using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class PreviewRevisionImpactEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/revision-impact-previews", async (PreviewRevisionImpactRequest request,
            CollectionPlatformStore store, IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
        {
            if (request.Revision is null)
                return Results.BadRequest(new { message = "Revision input is required." });
            var input = request.Revision;
            var preview = await store.PreviewRevisionImpactAsync(new(input.DefinitionId), input.Revision,
                CollectionPlatformEndpointSupport.BuildImpact(CollectionContractMapper.ToInternal(input.Impact)),
                conditions, token);
            return Results.Ok(new PreviewRevisionImpactResponse(CollectionContractMapper.ToDto(preview)));
        }).Produces<PreviewRevisionImpactResponse>(StatusCodes.Status200OK);
}
