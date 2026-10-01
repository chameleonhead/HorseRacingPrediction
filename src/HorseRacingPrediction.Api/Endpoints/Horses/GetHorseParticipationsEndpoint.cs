using EventFlow.EntityFramework;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HorseRacingPrediction.Contracts.Common;

namespace HorseRacingPrediction.Api.Endpoints.Horses;

using static HorseRacingPrediction.Api.Endpoints.Horses.HorseParticipationHistoryService;


internal static class GetHorseParticipationsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/horses/{horseId}/participations",
                    async ([AsParameters] HorseRacingPrediction.Contracts.Horses.GetHorseParticipationsRequest request, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        var horseId = request.HorseId;
                        using var dbContext = dbContextProvider.CreateContext();
                        if (!await dbContext.Horses.AsNoTracking().AnyAsync(x => x.HorseId == horseId, cancellationToken).ConfigureAwait(false)) return Results.NotFound();
                        var history = await BuildParticipationHistoryAsync("Horse", horseId, dbContext, request.Take, request.Skip, cancellationToken).ConfigureAwait(false);
                        return Results.Ok(new HorseRacingPrediction.Contracts.Horses.GetHorseParticipationsResponse(history));
                    })
                    .WithName("GetHorseParticipations")
                    .WithTags("Horse API")
                    .Produces<HorseRacingPrediction.Contracts.Horses.GetHorseParticipationsResponse>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
