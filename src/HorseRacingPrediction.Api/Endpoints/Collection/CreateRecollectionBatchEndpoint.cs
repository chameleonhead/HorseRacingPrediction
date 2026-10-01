using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CreateRecollectionBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/recollection-batches", async (CreateRecollectionBatchRequest request,
            CollectionPlatformStore store, IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
        {
            if (request.Batch is null)
                return Results.BadRequest(new { message = "Batch input is required." });
            var input = request.Batch;
            if (input.Mode == "Revision")
            {
                if (input.Definition is null || input.Revision is null || input.Provider is not null || input.From is not null || input.To is not null || input.BatchId is not null)
                    return Results.BadRequest(new { message = "Revision mode requires only definition and revision selectors." });
                var expansion = await store.ExpandRevisionRecollectionAsync(new(input.Definition), input.Revision.Value,
                    conditions, JstTime.Now(), input.Lane ?? CollectionLane.Background,
                    input.Priority ?? (int)CollectionPriority.Background, token);
                return Results.Accepted(value: new CreateRecollectionBatchResponse(
                    CollectionContractMapper.ToDto(new CollectionRecollectionBatchResponse(input.Mode, expansion, null))));
            }
            if (input.Mode == "RacePeriod")
            {
                if (input.Provider is null || input.From is null || input.To is null || input.Definition is not null || input.Revision is not null || input.Lane is not null || input.Priority is not null)
                    return Results.BadRequest(new { message = "RacePeriod mode requires provider, from, and to selectors." });
                var selector = new CreateRacePeriodRecollectionRequest(input.From.Value, input.To.Value, input.Provider, input.BatchId);
                var error = CollectionPlatformEndpointSupport.ValidateRacePeriodRecollection(selector);
                if (error is not null) return Results.BadRequest(new { message = error });
                var batchId = string.IsNullOrWhiteSpace(input.BatchId)
                    ? $"recollection:{input.From:yyyyMMdd}-{input.To:yyyyMMdd}:{Guid.NewGuid():N}" : input.BatchId.Trim();
                var result = await store.CreateOrResumeRacePeriodRecollectionAsync(batchId, input.Provider,
                    input.From.Value, input.To.Value, JstTime.Now(), token);
                return Results.Accepted($"/api/v2/admin/collection/backfill-batches/{Uri.EscapeDataString(batchId)}",
                    new CreateRecollectionBatchResponse(CollectionContractMapper.ToDto(
                        new CollectionRecollectionBatchResponse(input.Mode, null, result))));
            }
            return Results.BadRequest(new { message = "Mode must be Revision or RacePeriod." });
        })
        .WithName("CreateRecollectionBatch")
        .WithTags("Collection Platform")
        .WithDescription("mode is Revision or RacePeriod. Each mode accepts only its own selector fields.")
        .Produces<CreateRecollectionBatchResponse>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);
}
