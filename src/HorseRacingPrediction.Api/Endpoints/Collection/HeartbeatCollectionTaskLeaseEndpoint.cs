using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class HeartbeatCollectionTaskLeaseEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPatch(
        "/api/v2/internal/collection/tasks/{id:guid}/leases/{leaseId}", async (Guid id, string leaseId,
            HeartbeatCollectionTaskLeaseRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            if (request.Heartbeat is null)
                return Results.BadRequest(new { message = "Heartbeat input is required." });
            if (!string.Equals(leaseId, request.Heartbeat.LeaseToken, StringComparison.Ordinal)) return Results.Conflict();
            return await store.HeartbeatAsync(id, request.Heartbeat.LeaseToken, JstTime.Now(),
                TimeSpan.FromSeconds(Math.Clamp(request.Heartbeat.LeaseSeconds, 30, 3600)), token)
                ? Results.Ok(new HeartbeatCollectionTaskLeaseResponse(leaseId)) : Results.Conflict();
        }).Produces<HeartbeatCollectionTaskLeaseResponse>(StatusCodes.Status200OK);
}
