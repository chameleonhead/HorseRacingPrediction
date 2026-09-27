using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Contracts.Time;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CreateRaceEntryOwnerRepairBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/race-entry-owner-repair-batches", async (RaceEntryOwnerRepairRequest request,
            [FromServices] IDbContextProvider<EventStoreDbContext> provider,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            if (!(await store.GetPipelineStateAsync(token)).IsPaused)
                return Results.Conflict(new { message = "Use the paused race entry owner migration." });
            if ((await store.GetTasksAsync(CollectionTaskStatus.Running, 1, token)).Count > 0)
                return Results.Conflict(new { message = "Running collection tasks must be drained before migration." });
            var selected = request.RaceIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
            if (selected.Length is 0 or > 24) return Results.BadRequest(new { message = "Select between 1 and 24 races." });
            using var db = provider.CreateContext();
            var races = await db.Set<RacePredictionContextReadModel>().AsNoTracking()
                .Where(x => x.RaceDate == request.Date && selected.Contains(x.RaceId)).ToListAsync(token);
            var candidates = races.Select(CollectionPlatformEndpointSupport.ToRaceEntryOwnerRepairCandidate)
                .Where(x => x.MissingOwnerCount > 0).ToArray();
            if (candidates.Length != selected.Length)
                return Results.Conflict(new { message = "Selection changed after preview; preview again before executing." });
            var targets = candidates.Select(x => new CollectionBulkTarget(new(CollectionResourceType.Race, "JRA", x.ResourceId), request.Date,
                new Dictionary<string, string>
                {
                    ["domainRaceId"] = x.RaceId,
                    ["date"] = request.Date.ToString("yyyy-MM-dd"),
                    ["course"] = x.RacecourseCode,
                    ["number"] = x.RaceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["ownerRepair"] = "true"
                })).ToArray();
            var result = await store.ExecuteBulkRequestAsync(new("race-detail"), CollectionDefinitionRevisions.RaceDetail,
                CollectionReason.DefinitionChanged, targets, JstTime.Now(), CollectionPlatformEndpointSupport.RaceEntryOwnerMigrationBatchId,
                CollectionLane.Normal, (int)CollectionPriority.Normal, token);
            return Results.Accepted(value: new RaceEntryOwnerRepairReceipt(CollectionPlatformEndpointSupport.RaceEntryOwnerMigrationBatchId,
                result.TargetCount, result.TasksCreated, result.Requests.Where(x => x.TaskId.HasValue).Select(x => x.TaskId!.Value).ToArray()));
        });
}
