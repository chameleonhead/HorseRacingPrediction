using EventFlow.EntityFramework;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Infrastructure.Persistence;

namespace HorseRacingPrediction.Api;

public static partial class EndpointExtensions
{
    private static void MapIdentityResolutionEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/identity/horse", async (ResolveHorseIdentityRequest request, IDbContextProvider<EventStoreDbContext> provider, CancellationToken token) =>
        {
            using var db = provider.CreateContext();
            try { return Results.Ok(new ResolvedIdentity(await CollectionIdentityResolver.HorseAsync(db, request.Name, request.SourceIdentity, request.BirthDate, token))); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { code = ex.Message }); }
        });
        group.MapPost("/identity/race", async (ResolveRaceIdentityRequest request, IDbContextProvider<EventStoreDbContext> provider, CancellationToken token) =>
        {
            using var db = provider.CreateContext();
            try { return Results.Ok(new ResolvedIdentity(await CollectionIdentityResolver.RaceAsync(db, request.Date, request.Course, request.Number, token))); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { code = ex.Message }); }
        });
    }
}
