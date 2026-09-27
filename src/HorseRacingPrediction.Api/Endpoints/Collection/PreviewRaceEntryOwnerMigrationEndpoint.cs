using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class PreviewRaceEntryOwnerMigrationEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/migration-previews/race-entry-owner-repair",
        async ([FromServices] IDbContextProvider<EventStoreDbContext> db,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            var candidates = await CollectionPlatformEndpointSupport.GetRaceEntryOwnerMigrationCandidatesAsync(db, token);
            var batch = await store.GetBatchResourceStatusesAsync(CollectionPlatformEndpointSupport.RaceEntryOwnerMigrationBatchId, token);
            return Results.Ok(CollectionPlatformEndpointSupport.BuildRaceEntryOwnerMigrationProgress(candidates, batch));
        });
}
