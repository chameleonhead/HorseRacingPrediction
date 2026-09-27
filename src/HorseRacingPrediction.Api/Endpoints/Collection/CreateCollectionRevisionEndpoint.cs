using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CreateCollectionRevisionEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/definitions/{definition}/revisions", async (string definition,
            CreateCollectionRevisionRequest request, CollectionPlatformStore store,
            IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
        {
            var affected = await store.AddRevisionAndApplyImpactAsync(new(definition), request.Revision,
                request.Description, CollectionPlatformEndpointSupport.BuildImpact(request.Impact), conditions, JstTime.Now(), token);
            return Results.Ok(new CollectionRevisionApplyResult(definition, request.Revision, affected));
        });
}
internal sealed record CreateCollectionRevisionRequest(int Revision, string Description, RevisionImpactRequest Impact);
