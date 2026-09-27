using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CreateBackfillBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/backfill-batches", async (CreateBackfillBatchRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            if (request.Month is < 1 or > 12 || request.Year is < 1900 or > 2200)
                return Results.BadRequest(new { message = "Year and month are invalid." });
            var from = new DateOnly(request.Year, request.Month, 1);
            var to = from.AddMonths(1).AddDays(-1);
            var batchId = string.IsNullOrWhiteSpace(request.BatchId)
                ? $"{request.Provider.Trim().ToLowerInvariant()}:{request.Year:D4}-{request.Month:D2}" : request.BatchId;
            var batch = await store.CreateOrResumeBackfillBatchAsync(batchId, request.Provider, from, to, JstTime.Now(), token);
            return Results.Accepted($"/api/v2/admin/collection/backfill-batches/{Uri.EscapeDataString(batchId)}", batch);
        });
}
