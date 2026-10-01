using EventFlow.EntityFramework;
using HorseRacingPrediction.Infrastructure.Persistence;

using HorseRacingPrediction.Contracts.Identity;

namespace HorseRacingPrediction.Api.Endpoints.Identity;


internal static class ResolveHorseIdentityEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/identity/horse", async (ResolveHorseIdentityRequest request, IDbContextProvider<EventStoreDbContext> provider, CancellationToken token) =>
                {
                    if (request.Horse is not { } horse)
                        return Results.BadRequest(new[] { "馬の同定情報を指定してください。" });
                    using var db = provider.CreateContext();
                    try
                    {
                        return Results.Ok(new ResolveHorseIdentityResponse(new ResolvedIdentityDto(
                        await CollectionIdentityResolver.HorseAsync(db, horse.Name, horse.SourceIdentity, horse.BirthDate, token))));
                    }
                    catch (InvalidOperationException ex) when (SubjectIdentityResolutionException.IsKnownCode(ex.Message))
                    { return Results.UnprocessableEntity(new { code = ex.Message }); }
                    catch (InvalidOperationException ex) { return Results.Conflict(new { code = ex.Message }); }
                })
                .Produces<ResolveHorseIdentityResponse>(StatusCodes.Status200OK)
                .Produces<IEnumerable<string>>(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status409Conflict)
                .Produces(StatusCodes.Status422UnprocessableEntity);
    }
}
