using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CreateBackfillBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/backfill-batches", async (CreateBackfillBatchRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            if (request.Batch is null)
                return Results.BadRequest(new { message = "Backfill batch input is required." });
            var input = request.Batch;
            if (input.Month is < 1 or > 12 || input.Year is < 1900 or > 2200)
                return Results.BadRequest(new { message = "Year and month are invalid." });
            var from = new DateOnly(input.Year, input.Month, 1);
            var to = from.AddMonths(1).AddDays(-1);
            var batchId = string.IsNullOrWhiteSpace(input.BatchId)
                ? $"{input.Provider.Trim().ToLowerInvariant()}:{input.Year:D4}-{input.Month:D2}" : input.BatchId;
            var batch = await store.CreateOrResumeBackfillBatchAsync(batchId, input.Provider, from, to, JstTime.Now(), token);
            return Results.Accepted($"/api/v2/admin/collection/backfill-batches/{Uri.EscapeDataString(batchId)}",
                new CreateBackfillBatchResponse(CollectionContractMapper.ToDto(batch)));
        }).Produces<CreateBackfillBatchResponse>(StatusCodes.Status202Accepted);
}
