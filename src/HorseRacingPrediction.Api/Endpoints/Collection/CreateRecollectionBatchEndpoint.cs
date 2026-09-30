using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CreateRecollectionBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/recollection-batches", async (CollectionRecollectionBatchRequest request,
            CollectionPlatformStore store, IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
        {
            if (request.Mode == "Revision")
            {
                if (request.Definition is null || request.Revision is null || request.Provider is not null || request.From is not null || request.To is not null || request.BatchId is not null)
                    return Results.BadRequest(new { message = "Revision mode requires only definition and revision selectors." });
                var expansion = await store.ExpandRevisionRecollectionAsync(new(request.Definition), request.Revision.Value,
                    conditions, JstTime.Now(), request.Lane ?? CollectionLane.Background,
                    request.Priority ?? (int)CollectionPriority.Background, token);
                return Results.Accepted(value: new CollectionRecollectionBatchResponse(request.Mode, expansion, null));
            }
            if (request.Mode == "RacePeriod")
            {
                if (request.Provider is null || request.From is null || request.To is null || request.Definition is not null || request.Revision is not null || request.Lane is not null || request.Priority is not null)
                    return Results.BadRequest(new { message = "RacePeriod mode requires provider, from, and to selectors." });
                var selector = new CreateRacePeriodRecollectionRequest(request.From.Value, request.To.Value, request.Provider, request.BatchId);
                var error = CollectionPlatformEndpointSupport.ValidateRacePeriodRecollection(selector);
                if (error is not null) return Results.BadRequest(new { message = error });
                var batchId = string.IsNullOrWhiteSpace(request.BatchId)
                    ? $"recollection:{request.From:yyyyMMdd}-{request.To:yyyyMMdd}:{Guid.NewGuid():N}" : request.BatchId.Trim();
                var result = await store.CreateOrResumeRacePeriodRecollectionAsync(batchId, request.Provider,
                    request.From.Value, request.To.Value, JstTime.Now(), token);
                return Results.Accepted($"/api/v2/admin/collection/backfill-batches/{Uri.EscapeDataString(batchId)}",
                    new CollectionRecollectionBatchResponse(request.Mode, null, result));
            }
            return Results.BadRequest(new { message = "Mode must be Revision or RacePeriod." });
        })
        .WithName("CreateRecollectionBatch")
        .WithTags("Collection Platform")
        .WithDescription("mode is Revision or RacePeriod. Each mode accepts only its own selector fields.")
        .Produces<CollectionRecollectionBatchResponse>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);
}
