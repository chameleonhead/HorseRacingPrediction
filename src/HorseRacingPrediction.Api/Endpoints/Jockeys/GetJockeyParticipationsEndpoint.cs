using EventFlow.EntityFramework;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Api.Endpoints.Jockeys;

using static HorseRacingPrediction.Api.Endpoints.Jockeys.JockeyParticipationHistoryService;


internal static class GetJockeyParticipationsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/jockeys/{jockeyId}/participations",
                    async (string jockeyId, int? take, int? skip, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        using var dbContext = dbContextProvider.CreateContext();
                        if (!await dbContext.Jockeys.AsNoTracking().AnyAsync(x => x.JockeyId == jockeyId, cancellationToken).ConfigureAwait(false)) return Results.NotFound();
                        return Results.Ok(await BuildParticipationHistoryAsync("Jockey", jockeyId, dbContext, take, skip, cancellationToken).ConfigureAwait(false));
                    })
                    .WithName("GetJockeyParticipations")
                    .WithTags("Jockey API")
                    .Produces<ParticipationHistoryDto>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
