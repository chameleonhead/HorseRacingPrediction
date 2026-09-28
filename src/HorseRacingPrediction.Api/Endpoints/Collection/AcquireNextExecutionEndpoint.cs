using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts.Time;
using Microsoft.Extensions.Options;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class AcquireNextExecutionEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/internal/collection/execution-leases", async (CollectionExecutionAcquireRequest request,
            CollectionPlatformStore store, IOptions<CollectionQueueOptions> queueOptions, CancellationToken token) =>
        {
            var lease = await store.AcquireNextExecutionAsync(request.Wake, request.QueueMessageId, JstTime.Now(),
                TimeSpan.FromSeconds(45), token,
                aggregationDelayMilliseconds: queueOptions.Value.AggregationDelayMilliseconds);
            return lease is null ? Results.NoContent() : Results.Json(lease, statusCode: StatusCodes.Status201Created);
        });
}
