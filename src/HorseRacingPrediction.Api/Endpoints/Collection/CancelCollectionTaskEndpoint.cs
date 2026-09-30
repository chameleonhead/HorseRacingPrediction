using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal sealed record CollectionTaskCancellationRequest(bool CancellationRequested);

internal static class CancelCollectionTaskEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPatch("/api/v2/admin/collection/tasks/{taskId:guid}",
            async (Guid taskId, CollectionTaskCancellationRequest request,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                if (!request.CancellationRequested)
                    return Results.BadRequest(new { message = "CancellationRequested must be true." });
                return await store.CancelTaskAsync(taskId, JstTime.Now(), token)
                    ? Results.NoContent() : Results.Conflict();
            });
}
