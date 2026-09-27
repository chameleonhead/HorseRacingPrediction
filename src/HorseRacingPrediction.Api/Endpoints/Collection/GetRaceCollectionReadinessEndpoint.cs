using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetRaceCollectionReadinessEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/races/{raceId}/readiness",
            async (string raceId, CollectionPlatformStore store, CancellationToken token) =>
                Results.Ok(await store.GetReadinessAsync(raceId, token)));
}
