using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using HorseRacingPrediction.Application.Queries.ReadModels;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class ListRaceEntryOwnerRepairCandidatesEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet(
        "/api/v2/admin/collection/race-entry-owner-repair-candidates", async (DateOnly date,
            IDbContextProvider<EventStoreDbContext> provider, CancellationToken token) =>
        {
            using var db = provider.CreateContext();
            var races = await db.Set<RacePredictionContextReadModel>().AsNoTracking().Where(x => x.RaceDate == date)
                .OrderBy(x => x.RacecourseCode).ThenBy(x => x.RaceNumber).ToListAsync(token);
            var candidates = races.Select(CollectionPlatformEndpointSupport.ToRaceEntryOwnerRepairCandidate)
                .Where(x => x.MissingOwnerCount > 0 && x.Eligibility == RaceEntryOwnerRepairEligibility.CardRetrievalCandidate).ToArray();
            return Results.Ok(new RaceEntryOwnerRepairPreview(date, races.Count, candidates));
        });
}
