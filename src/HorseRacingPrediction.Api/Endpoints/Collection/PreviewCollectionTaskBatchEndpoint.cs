using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class PreviewCollectionTaskBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/task-batch-previews", async (PreviewCollectionTaskBatchRequest request,
            CollectionPlatformStore store, [FromServices] IDbContextProvider<EventStoreDbContext> domain,
            [FromServices] IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
        {
            if (request.Selection is null)
                return Results.BadRequest(new { message = "Selection input is required." });
            var input = CollectionContractMapper.ToInternal(request.Selection);
            var targets = await CollectionPlatformEndpointSupport.ResolveBulkTargetsAsync(input, store, domain, conditions, token);
            var preview = await store.PreviewBulkRequestAsync(new(input.DefinitionId), input.RequestedRevision, targets, token);
            return Results.Ok(new PreviewCollectionTaskBatchResponse(CollectionContractMapper.ToDto(preview)));
        }).Produces<PreviewCollectionTaskBatchResponse>(StatusCodes.Status200OK);
}
