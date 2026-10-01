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
            if (request.Pipeline is null)
                return Results.BadRequest(new { message = "Pipeline input is required." });
            if (request.Pipeline.Paused && string.IsNullOrWhiteSpace(request.Pipeline.Reason))
                return Results.BadRequest(new { message = "A reason is required when pausing the pipeline." });
            await store.SetPausedAsync(request.Pipeline.Paused,
                request.Pipeline.Paused ? request.Pipeline.Reason : null, JstTime.Now(), token);
            return Results.NoContent();
        }).AddEndpointFilter(async (context, next) =>
        {
            var request = context.Arguments.OfType<SetCollectionPipelineRequest>().Single();
            if (request.Pipeline?.Paused is true) return await next(context);
            var filter = context.HttpContext.RequestServices.GetRequiredService<global::HorseRacingPrediction.Api.Security.RaceWriteEndpointFilter>();
            return await filter.InvokeAsync(context, next);
        });
}
