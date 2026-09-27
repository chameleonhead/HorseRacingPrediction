using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class PreviewCollectionTaskBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/task-batch-previews", async (BulkCollectionOperationRequest request,
            CollectionPlatformStore store, IDbContextProvider<EventStoreDbContext> domain,
            IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
        {
            var targets = await CollectionPlatformEndpointSupport.ResolveBulkTargetsAsync(request, store, domain, conditions, token);
            return Results.Ok(await store.PreviewBulkRequestAsync(new(request.DefinitionId), request.RequestedRevision, targets, token));
        });
}
