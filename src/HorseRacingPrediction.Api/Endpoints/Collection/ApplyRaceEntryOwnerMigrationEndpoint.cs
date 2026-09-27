using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Contracts.Time;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class ApplyRaceEntryOwnerMigrationEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/migrations/race-entry-owner-repair",
        async ([FromServices] IDbContextProvider<EventStoreDbContext> db,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            if (!(await store.GetPipelineStateAsync(token)).IsPaused)
                return Results.Conflict(new { message = "Collection pipeline must be paused." });
            if ((await store.GetTasksAsync(CollectionTaskStatus.Running, 1, token)).Count > 0)
                return Results.Conflict(new { message = "Running collection tasks must be drained before migration." });
            var candidates = await CollectionPlatformEndpointSupport.GetRaceEntryOwnerMigrationCandidatesAsync(db, token);
            var actionable = candidates.Where(x => x.Eligibility == RaceEntryOwnerRepairEligibility.CardRetrievalCandidate).ToArray();
            if (actionable.Length > 0)
                await store.ExecuteBulkRequestAsync(new("race-detail"), CollectionDefinitionRevisions.RaceDetail,
                    CollectionReason.DefinitionChanged, actionable.Select(x => new CollectionBulkTarget(
                        new(CollectionResourceType.Race, "JRA", x.ResourceId), x.Date,
                        new Dictionary<string, string>
                        {
                            ["domainRaceId"] = x.RaceId,
                            ["date"] = x.Date.ToString("yyyy-MM-dd"),
                            ["course"] = x.RacecourseCode,
                            ["number"] = x.RaceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["ownerRepair"] = "true"
                        })),
                    JstTime.Now(), CollectionPlatformEndpointSupport.RaceEntryOwnerMigrationBatchId,
                    CollectionLane.Normal, (int)CollectionPriority.Normal, token);
            var batch = await store.GetBatchResourceStatusesAsync(CollectionPlatformEndpointSupport.RaceEntryOwnerMigrationBatchId, token);
            return Results.Ok(CollectionPlatformEndpointSupport.BuildRaceEntryOwnerMigrationProgress(candidates, batch));
        });
}
