using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetCollectionMonitoringFindingsEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/operations/monitoring-findings",
            async (CollectionMonitoringService service, CancellationToken token) =>
                Results.Ok(await service.InspectAsync(JstTime.Now(), token)));
}
