using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class HeartbeatCollectionTaskLeaseEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPatch(
        "/api/v2/internal/collection/tasks/{id:guid}/leases/{leaseId}", async (Guid id, string leaseId,
            HeartbeatCollectionTaskRequest request, CollectionPlatformStore store, CancellationToken token) =>
        {
            if (!string.Equals(leaseId, request.LeaseToken, StringComparison.Ordinal)) return Results.Conflict();
            return await store.HeartbeatAsync(id, request.LeaseToken, JstTime.Now(),
                TimeSpan.FromSeconds(Math.Clamp(request.LeaseSeconds, 30, 3600)), token)
                ? Results.Ok(new { leaseId }) : Results.Conflict();
        });
}
