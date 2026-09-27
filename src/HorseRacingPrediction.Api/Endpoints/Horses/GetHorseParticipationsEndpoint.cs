using EventFlow.EntityFramework;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

using static HorseRacingPrediction.Api.Endpoints.Horses.HorseParticipationHistoryService;


internal static class GetHorseParticipationsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/horses/{horseId}/participations",
                    async (string horseId, int? take, int? skip, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        using var dbContext = dbContextProvider.CreateContext();
                        if (!await dbContext.Horses.AsNoTracking().AnyAsync(x => x.HorseId == horseId, cancellationToken).ConfigureAwait(false)) return Results.NotFound();
                        return Results.Ok(await BuildParticipationHistoryAsync("Horse", horseId, dbContext, take, skip, cancellationToken).ConfigureAwait(false));
                    })
                    .WithName("GetHorseParticipations")
                    .WithTags("Horse API")
                    .Produces<ParticipationHistoryResponse>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
