using HorseRacingPrediction.Contracts.Time;

namespace HorseRacingPrediction.Api.CollectionController;

public static class CollectionMonitoringEndpointExtensions
{
    public static IEndpointRouteBuilder MapCollectionMonitoringEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/collection/monitoring")
            .WithTags("Collection Monitoring");
        group.MapGet("/findings", async (CollectionMonitoringService service, CancellationToken token) =>
            Results.Ok(await service.InspectAsync(JstTime.Now(), token)));
        group.MapGet("/known-recovery/preview", async (CollectionMonitoringService service,
            CancellationToken token) => Results.Ok(await service.PreviewKnownRecoveryAsync(JstTime.Now(), token)));
        group.MapPost("/known-recovery/apply", async (CollectionMonitoringService service,
            ILogger<CollectionMonitoringService> logger, CancellationToken token) =>
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
        return endpoints;
    }
}
