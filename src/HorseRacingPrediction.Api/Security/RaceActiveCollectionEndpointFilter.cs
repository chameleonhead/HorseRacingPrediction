using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Security;

public sealed class RaceActiveCollectionEndpointFilter(CollectionPlatformStore store) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var raceId = ResolveRaceId(context);
        if (raceId is null) return await next(context).ConfigureAwait(false);
        if (Guid.TryParse(context.HttpContext.Request.Headers["X-Collection-Task-Id"], out var taskId)
            && context.HttpContext.Request.Headers.TryGetValue("X-Collection-Lease-Token", out var leaseToken)
            && await store.IsValidActiveRaceLeaseAsync(taskId, leaseToken.ToString(), raceId,
                context.HttpContext.RequestAborted).ConfigureAwait(false))
            return await next(context).ConfigureAwait(false);
        if (await store.HasActiveRaceMutationAsync(raceId,
                context.HttpContext.RequestAborted).ConfigureAwait(false))
            return Results.Conflict(new { message = "収集処理中のためレース情報を変更できません。" });
        return await next(context).ConfigureAwait(false);
    }

    private static string? ResolveRaceId(EndpointFilterInvocationContext context)
    {
        var routeId = context.HttpContext.Request.RouteValues["raceId"]?.ToString();
        if (!string.IsNullOrWhiteSpace(routeId)) return routeId;
        foreach (var argument in context.Arguments.Where(x => x is not null))
        {
            var type = argument!.GetType();
            var target = type.GetProperty("TargetRaceId")?.GetValue(argument)?.ToString();
            if (!string.IsNullOrWhiteSpace(target)) return target;
            if (type.GetProperty("RaceDate")?.GetValue(argument) is DateOnly date
                && type.GetProperty("RacecourseCode")?.GetValue(argument) is string course
                && type.GetProperty("RaceNumber")?.GetValue(argument) is int number)
                return HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildRaceId(date, course, number);
        }
        return null;
    }
}
