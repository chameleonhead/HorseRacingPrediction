using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Security;

public sealed class RaceActiveCollectionEndpointFilter(CollectionPlatformStore store) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (context.HttpContext.Request.Headers.TryGetValue("X-Collection-Worker", out var worker)
            && string.Equals(worker.ToString(), "true", StringComparison.OrdinalIgnoreCase))
            return await next(context).ConfigureAwait(false);
        var raceId = context.HttpContext.Request.RouteValues["raceId"]?.ToString();
        if (raceId is not null && await store.HasActiveRaceMutationAsync(raceId,
                context.HttpContext.RequestAborted).ConfigureAwait(false))
            return Results.Conflict(new { message = "収集処理中のためレース情報を変更できません。" });
        return await next(context).ConfigureAwait(false);
    }
}
