using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;


namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class RecoverBackfillHolesEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v2/admin/collection/backfill-batches/{id}/recovery-batches",
            async (string id, CollectionPlatformStore store, CancellationToken token) =>
            {
                var batch = await store.GetBackfillBatchAsync(id, token);
                if (batch is null) return Results.NotFound();
                var created = 0;
                var recoveryBatchId = $"recovery:{id}:{Guid.NewGuid():N}";
                foreach (var hole in batch.Holes)
                {
                    var state = await store.GetStateAsync(hole.Resource, hole.Definition, token);
                    var receipt = await store.RequestAsync(hole.Resource, hole.Definition,
                        Math.Max(1, state?.RequiredRevision ?? state?.AppliedRevision ?? 1), CollectionReason.Recovery,
                        JstTime.Now(), CollectionLane.Background, (int)CollectionPriority.Background,
                        batchId: recoveryBatchId, cancellationToken: token);
                    if (receipt.CreatedTask) created++;
                }
                return Results.Accepted(value: new BackfillHoleRecoveryResult(batch.Holes.Count, created));
            });
}
