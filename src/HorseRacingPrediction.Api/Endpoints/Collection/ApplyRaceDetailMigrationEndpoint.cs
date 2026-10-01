using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
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
            var response = new ApplyRaceDetailMigrationResponse(CollectionContractMapper.ToDto(report));
            return report.Errors.Count == 0 ? Results.Ok(response) : Results.Conflict(response);
        })
        .Produces<ApplyRaceDetailMigrationResponse>(StatusCodes.Status200OK)
        .Produces<ApplyRaceDetailMigrationResponse>(StatusCodes.Status409Conflict);
}
