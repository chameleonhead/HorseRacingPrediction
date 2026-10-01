using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.AspNetCore.Mvc;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class ListRaceEntryOwnerRepairCandidatesEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet(
        "/api/v2/admin/collection/race-entry-owner-repair-candidates", async ([AsParameters] ListRaceEntryOwnerRepairCandidatesRequest request,
            [FromServices] IDbContextProvider<EventStoreDbContext> provider, CancellationToken token) =>
        {
            var date = request.Date;
            using var db = provider.CreateContext();
            var races = await db.Set<RacePredictionContextReadModel>().AsNoTracking().Where(x => x.RaceDate == date)
                .OrderBy(x => x.RacecourseCode).ThenBy(x => x.RaceNumber).ToListAsync(token);
            var candidates = races.Select(CollectionPlatformEndpointSupport.ToRaceEntryOwnerRepairCandidate)
                .Where(x => x.MissingOwnerCount > 0 && x.Eligibility == RaceEntryOwnerRepairEligibility.CardRetrievalCandidate).ToArray();
            return Results.Ok(new ListRaceEntryOwnerRepairCandidatesResponse(
                CollectionContractMapper.ToDto(new RaceEntryOwnerRepairPreview(date, races.Count, candidates))));
        }).Produces<ListRaceEntryOwnerRepairCandidatesResponse>(StatusCodes.Status200OK);
}
