using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Time;
namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class ApplyRaceDetailMigrationEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v2/admin/collection/migrations/race-detail",
        async (CollectionPlatformStore store, CancellationToken token) =>
        {
            if (!(await store.GetPipelineStateAsync(token)).IsPaused)
                return Results.Conflict(new { message = "Collection pipeline must be paused." });
            var report = await store.MergeLegacyRaceDetailsAsync(true, JstTime.Now(), token);
            return report.Errors.Count == 0 ? Results.Ok(report) : Results.Conflict(report);
        });
}
