using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
using HorseRacingPrediction.Infrastructure.Persistence;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class AcquireCollectionTaskEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/internal/collection/tasks/{id:guid}/leases", async (Guid id, AcquireCollectionTaskRequest request,
            CollectionPlatformStore store, IServiceProvider services, CancellationToken token) =>
        {
            if (request.Correlation is not null && !request.Correlation.IsSupported())
                return Results.BadRequest(new { Error = "The collection attempt correlation is invalid." });
            var lease = await store.AcquireAsync(id, request.DispatchGeneration, JstTime.Now(),
                TimeSpan.FromSeconds(Math.Clamp(request.LeaseSeconds, 30, 3600)), request.Correlation, token);
            if (lease is not null && (lease.RaceHoldGeneration > 0 || lease.Definition.Value == "race-odds"))
            {
                var coordinator = services.GetRequiredService<RaceWriteCoordinator>();
                var raceId = services.GetRequiredService<IRaceResourceIdentityResolver>().Resolve(lease.Resource.Id, lease.Attributes);
                if (raceId is null) return Results.Ok(new CollectionTaskAcquireResult(CollectionTaskAcquireStatus.RepairHeld));
                await using var held = await coordinator.AcquireAsync([raceId], token);
                if (!await store.IsValidActiveRaceLeaseAsync(lease.TaskId, lease.LeaseToken, raceId, token))
                    return Results.Ok(new CollectionTaskAcquireResult(CollectionTaskAcquireStatus.RepairHeld));
                lease = lease with { EntryAssignmentFingerprint = await coordinator.AssignmentFingerprintAsync(raceId, token) };
            }
            if (lease is not null) return Results.Created($"/api/v2/internal/collection/tasks/{id}/leases/{Uri.EscapeDataString(lease.LeaseToken)}",
                new CollectionTaskAcquireResult(CollectionTaskAcquireStatus.Acquired, lease));
            return Results.Ok(new CollectionTaskAcquireResult(await store.ClassifyAcquireFailureAsync(id, request.DispatchGeneration, token)));
        });
}
