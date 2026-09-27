using EventFlow.EntityFramework;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Infrastructure.Persistence;

namespace HorseRacingPrediction.Api.Endpoints.Identity;


internal static class ResolveRaceIdentityEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/identity/race", async (ResolveRaceIdentityRequest request, IDbContextProvider<EventStoreDbContext> provider, CancellationToken token) =>
                {
                    using var db = provider.CreateContext();
                    try { return Results.Ok(new ResolvedIdentity(await CollectionIdentityResolver.RaceAsync(db, request.Date, request.Course, request.Number, token))); }
                    catch (InvalidOperationException ex) { return Results.Conflict(new { code = ex.Message }); }
                });
    }
}
