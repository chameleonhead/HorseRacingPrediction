using EventFlow.EntityFramework;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Repairs;

using static HorseRacingPrediction.Api.Endpoints.Repairs.HorseIdentityRepairService;


internal static class GetHorseIdentityRepairEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/admin/repairs/20260913-jra-horse-identity",
                    async (IDbContextProvider<EventStoreDbContext> provider, CollectionPlatformStore collectionStore,
                        CancellationToken token) =>
                    {
                        using var db = provider.CreateContext();
                        return Results.Ok(new HorseRacingPrediction.Contracts.Repairs.GetHorseIdentityRepairResponse(
                            await BuildHorseIdentityRepairPreviewAsync(db, collectionStore, token).ConfigureAwait(false)));
                    })
                    .Produces<HorseRacingPrediction.Contracts.Repairs.GetHorseIdentityRepairResponse>(StatusCodes.Status200OK);
    }
}
