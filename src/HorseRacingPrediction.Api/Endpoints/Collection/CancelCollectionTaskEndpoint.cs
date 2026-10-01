using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CancelCollectionTaskEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPatch("/api/v2/admin/collection/tasks/{taskId:guid}",
            async (Guid taskId, CancelCollectionTaskRequest request,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                if (request.Cancellation is null || !request.Cancellation.CancellationRequested)
                    return Results.BadRequest(new { message = "CancellationRequested must be true." });
                return await store.CancelTaskAsync(taskId, JstTime.Now(), token)
                    ? Results.NoContent() : Results.Conflict();
            });
}
