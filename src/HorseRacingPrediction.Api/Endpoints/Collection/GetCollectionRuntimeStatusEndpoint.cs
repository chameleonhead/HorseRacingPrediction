using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Contracts.Collection;
using Microsoft.AspNetCore.Mvc;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetCollectionRuntimeStatusEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/operations/runtime-status",
            ([FromServices] CollectionRuntimeStatusRecorder recorder, HttpContext context, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                context.Response.Headers.CacheControl = "no-store";
                GetCollectionRuntimeStatusResponse response = recorder.GetSnapshot();
                return Results.Ok(response);
            })
            .Produces<GetCollectionRuntimeStatusResponse>(StatusCodes.Status200OK);
}
