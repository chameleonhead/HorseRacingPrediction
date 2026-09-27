using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CreateKnownRecoveryBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v2/admin/collection/known-recovery-batches",
            async (CollectionMonitoringService service, ILogger<CollectionMonitoringService> logger,
                CancellationToken token) =>
            {
                try
                {
                    return Results.Ok(await service.ApplyKnownRecoveryAsync(JstTime.Now(), logger, token));
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { message = exception.Message });
                }
            });
}
