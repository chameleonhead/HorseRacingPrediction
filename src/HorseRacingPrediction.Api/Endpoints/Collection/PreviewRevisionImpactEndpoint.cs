using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class PreviewRevisionImpactEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/revision-impact-previews", async (RevisionImpactPreviewRequest request,
            CollectionPlatformStore store, IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
            Results.Ok(await store.PreviewRevisionImpactAsync(new(request.DefinitionId), request.Revision,
                CollectionPlatformEndpointSupport.BuildImpact(request.Impact), conditions, token)));
}
