using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class SetCollectionPipelineEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPut("/api/v2/admin/collection/pipeline", async (SetCollectionPipelineRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            if (request.Paused && string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { message = "A reason is required when pausing the pipeline." });
            await store.SetPausedAsync(request.Paused, request.Paused ? request.Reason : null, JstTime.Now(), token);
            return Results.NoContent();
        }).AddEndpointFilter(async (context, next) =>
        {
            var request = context.Arguments.OfType<SetCollectionPipelineRequest>().Single();
            if (request.Paused) return await next(context);
            var filter = context.HttpContext.RequestServices.GetRequiredService<global::HorseRacingPrediction.Api.Security.RaceWriteEndpointFilter>();
            return await filter.InvokeAsync(context, next);
        });
}

internal sealed record SetCollectionPipelineRequest(bool Paused, string? Reason = null);
