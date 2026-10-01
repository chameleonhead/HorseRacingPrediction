using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts.Common.Time;
using Microsoft.AspNetCore.Mvc;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CreateKnownRecoveryBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v2/admin/collection/known-recovery-batches",
            async ([FromServices] CollectionMonitoringService service,
                [FromServices] ILogger<CollectionMonitoringService> logger,
                CancellationToken token) =>
            {
                try
                {
                    return Results.Ok(new CreateKnownRecoveryBatchResponse(CollectionContractMapper.ToDto(
                        await service.ApplyKnownRecoveryAsync(JstTime.Now(), logger, token))));
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { message = exception.Message });
                }
            }).Produces<CreateKnownRecoveryBatchResponse>(StatusCodes.Status200OK);
}
