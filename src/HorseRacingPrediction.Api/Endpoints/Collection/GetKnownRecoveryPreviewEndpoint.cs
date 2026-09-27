using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetKnownRecoveryPreviewEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/recovery-previews/known",
            async (CollectionMonitoringService service, CancellationToken token) =>
                Results.Ok(await service.PreviewKnownRecoveryAsync(JstTime.Now(), token)));
}
