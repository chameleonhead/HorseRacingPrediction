using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetRevisionRecollectionProgressEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet(
        "/api/v2/admin/collection/recollection-batches", async (string definition, int revision,
            CollectionPlatformStore store, IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
            Results.Ok(await store.GetRevisionRecollectionProgressAsync(new(definition), revision, conditions, token)));
}
