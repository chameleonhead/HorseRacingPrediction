using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class PreviewRaceDetailMigrationEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/migration-previews/race-detail",
        async (CollectionPlatformStore store, CancellationToken token) =>
            Results.Ok(await store.MergeLegacyRaceDetailsAsync(false, JstTime.Now(), token)));
}
