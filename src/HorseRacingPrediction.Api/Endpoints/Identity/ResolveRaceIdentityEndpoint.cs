using EventFlow.EntityFramework;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Infrastructure.Persistence;

namespace HorseRacingPrediction.Api.Endpoints.Identity;


internal static class ResolveRaceIdentityEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/identity/race", async (HorseRacingPrediction.Contracts.Identity.ResolveRaceIdentityRequest request, IDbContextProvider<EventStoreDbContext> provider, CancellationToken token) =>
                {
                    if (request.Race is not { } race)
                        return Results.BadRequest(new[] { "レースの同定情報を指定してください。" });
                    using var db = provider.CreateContext();
                    try
                    {
                        return Results.Ok(new HorseRacingPrediction.Contracts.Identity.ResolveRaceIdentityResponse(
                        new HorseRacingPrediction.Contracts.Identity.ResolvedIdentityDto(
                            await CollectionIdentityResolver.RaceAsync(db, race.Date, race.Course, race.Number, token))));
                    }
                    catch (InvalidOperationException ex) { return Results.Conflict(new { code = ex.Message }); }
                })
                .Produces<HorseRacingPrediction.Contracts.Identity.ResolveRaceIdentityResponse>(StatusCodes.Status200OK)
                .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status409Conflict);
    }
}
