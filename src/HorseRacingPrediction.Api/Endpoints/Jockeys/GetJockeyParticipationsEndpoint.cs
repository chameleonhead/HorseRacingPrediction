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
                    async ([AsParameters] HorseRacingPrediction.Contracts.Jockeys.GetJockeyParticipationsRequest request, IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken cancellationToken) =>
                    {
                        var jockeyId = request.JockeyId;
                        using var dbContext = dbContextProvider.CreateContext();
                        if (!await dbContext.Jockeys.AsNoTracking().AnyAsync(x => x.JockeyId == jockeyId, cancellationToken).ConfigureAwait(false)) return Results.NotFound();
                        var history = await BuildParticipationHistoryAsync("Jockey", jockeyId, dbContext, request.Take, request.Skip, cancellationToken).ConfigureAwait(false);
                        return Results.Ok(new HorseRacingPrediction.Contracts.Jockeys.GetJockeyParticipationsResponse(history));
                    })
                    .WithName("GetJockeyParticipations")
                    .WithTags("Jockey API")
                    .Produces<HorseRacingPrediction.Contracts.Jockeys.GetJockeyParticipationsResponse>(StatusCodes.Status200OK)
                    .Produces(StatusCodes.Status404NotFound);
    }
}
