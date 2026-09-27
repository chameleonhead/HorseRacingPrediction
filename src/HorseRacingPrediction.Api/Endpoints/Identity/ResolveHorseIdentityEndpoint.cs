using EventFlow.EntityFramework;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Infrastructure.Persistence;

namespace HorseRacingPrediction.Api.Endpoints.Identity;


internal static class ResolveHorseIdentityEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/identity/horse", async (ResolveHorseIdentityRequest request, IDbContextProvider<EventStoreDbContext> provider, CancellationToken token) =>
                {
                    using var db = provider.CreateContext();
                    try { return Results.Ok(new ResolvedIdentity(await CollectionIdentityResolver.HorseAsync(db, request.Name, request.SourceIdentity, request.BirthDate, token))); }
                    catch (InvalidOperationException ex) when (SubjectIdentityResolutionException.IsKnownCode(ex.Message))
                    { return Results.UnprocessableEntity(new { code = ex.Message }); }
                    catch (InvalidOperationException ex) { return Results.Conflict(new { code = ex.Message }); }
                });
    }
}
