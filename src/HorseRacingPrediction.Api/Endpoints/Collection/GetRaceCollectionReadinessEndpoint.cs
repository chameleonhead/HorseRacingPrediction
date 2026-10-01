using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetRaceCollectionReadinessEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/races/{raceId}/readiness",
            async ([AsParameters] GetRaceCollectionReadinessRequest request, CollectionPlatformStore store, CancellationToken token) =>
                Results.Ok(new GetRaceCollectionReadinessResponse(CollectionContractMapper.ToDto(
                    await store.GetReadinessAsync(request.RaceId, token)))))
            .Produces<GetRaceCollectionReadinessResponse>(StatusCodes.Status200OK);
}
