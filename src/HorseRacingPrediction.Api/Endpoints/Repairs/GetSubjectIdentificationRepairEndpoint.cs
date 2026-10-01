using EventFlow.EntityFramework;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;

using HorseRacingPrediction.Contracts.Repairs;

namespace HorseRacingPrediction.Api.Endpoints.Repairs;

using static HorseRacingPrediction.Api.Endpoints.Repairs.SubjectIdentificationRepairService;


internal static class GetSubjectIdentificationRepairEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/admin/repairs/subject-identification",
                    async (IDbContextProvider<EventStoreDbContext> provider, CollectionPlatformStore collectionStore,
                        CancellationToken token) =>
                    {
                        using var db = provider.CreateContext();
                        var candidates = await BuildSubjectIdentificationRepairPreviewAsync(db, collectionStore, token)
                            .ConfigureAwait(false);
                        return Results.Ok(new GetSubjectIdentificationRepairResponse(new SubjectIdentificationRepairPreviewDto(candidates)));
                    })
                    .Produces<GetSubjectIdentificationRepairResponse>(StatusCodes.Status200OK);
    }
}
