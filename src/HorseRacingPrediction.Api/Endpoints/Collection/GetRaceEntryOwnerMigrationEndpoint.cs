using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetRaceEntryOwnerMigrationEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/migrations/race-entry-owner-repair",
            async (IDbContextProvider<EventStoreDbContext> dbContextProvider,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                var candidates = await CollectionPlatformEndpointSupport
                    .GetRaceEntryOwnerMigrationCandidatesAsync(dbContextProvider, token);
                var batch = await store.GetBatchResourceStatusesAsync(
                    CollectionPlatformEndpointSupport.RaceEntryOwnerMigrationBatchId, token);
                return Results.Ok(CollectionPlatformEndpointSupport
                    .BuildRaceEntryOwnerMigrationProgress(candidates, batch));
            });
}
