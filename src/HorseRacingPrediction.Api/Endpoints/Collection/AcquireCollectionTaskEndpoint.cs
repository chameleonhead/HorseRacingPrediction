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
            if (request.Acquisition is null)
                return Results.BadRequest(new { Error = "The collection task acquisition is invalid." });
            var input = request.Acquisition;
            var correlation = input.Correlation is null ? null : new CollectionAttemptCorrelation(
                input.Correlation.ExecutionBatchId, input.Correlation.DispatchEnvelopeId,
                input.Correlation.QueueMessageId, input.Correlation.LambdaRequestId,
                input.Correlation.BatchTaskOrdinal, input.Correlation.BatchTaskCount);
            if (correlation is not null && !correlation.IsSupported())
                return Results.BadRequest(new { Error = "The collection attempt correlation is invalid." });
            var lease = await store.AcquireAsync(id, input.DispatchGeneration, JstTime.Now(),
                TimeSpan.FromSeconds(Math.Clamp(input.LeaseSeconds, 30, 3600)), correlation, token);
            if (lease is not null && (lease.RaceHoldGeneration > 0 || lease.Definition.Value == "race-odds"))
            {
                var coordinator = services.GetRequiredService<RaceWriteCoordinator>();
                var raceId = services.GetRequiredService<IRaceResourceIdentityResolver>().Resolve(lease.Resource.Id, lease.Attributes);
                if (raceId is null) return Results.Ok(new AcquireCollectionTaskResponse(
                    new(CollectionTaskAcquireStatus.RepairHeld)));
                await using var held = await coordinator.AcquireAsync([raceId], token);
                if (!await store.IsValidActiveRaceLeaseAsync(lease.TaskId, lease.LeaseToken, raceId, token))
                    return Results.Ok(new AcquireCollectionTaskResponse(new(CollectionTaskAcquireStatus.RepairHeld)));
                lease = lease with { EntryAssignmentFingerprint = await coordinator.AssignmentFingerprintAsync(raceId, token) };
            }
            if (lease is not null) return Results.Created($"/api/v2/internal/collection/tasks/{id}/leases/{Uri.EscapeDataString(lease.LeaseToken)}",
                new AcquireCollectionTaskResponse(CollectionContractMapper.ToDto(
                    new CollectionTaskAcquireResult(CollectionTaskAcquireStatus.Acquired, lease))));
            return Results.Ok(new AcquireCollectionTaskResponse(CollectionContractMapper.ToDto(
                new CollectionTaskAcquireResult(await store.ClassifyAcquireFailureAsync(id,
                    input.DispatchGeneration, token)))));
        })
        .Produces<AcquireCollectionTaskResponse>(StatusCodes.Status200OK)
        .Produces<AcquireCollectionTaskResponse>(StatusCodes.Status201Created);
}
