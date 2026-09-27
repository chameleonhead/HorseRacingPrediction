using HorseRacingPrediction.Api.CollectionController;
using Microsoft.AspNetCore.Mvc;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetOwnerIdentityMigrationPreviewEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/migration-previews/owner-identity",
            async ([FromServices] CollectionMonitoringService service, CancellationToken token) =>
                Results.Ok(await service.PreviewOwnerIdentityMigrationAsync(token)));
}
