using EventFlow.Queries;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Api.Security;

public sealed class RaceMutationLeaseEndpointFilter(
    ProcessingStateStore stateStore,
    IQueryProcessor queryProcessor) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!request.Path.StartsWithSegments("/api/races") || HttpMethods.IsGet(request.Method))
            return await next(context);

        var body = context.Arguments.FirstOrDefault(x => x is not null && x.GetType().GetProperty("RaceDate") is not null);
        var raceDate = body?.GetType().GetProperty("RaceDate")?.GetValue(body) as DateOnly?;
        var raceId = context.HttpContext.Request.RouteValues.TryGetValue("raceId", out var routeId)
            ? routeId?.ToString()
            : body?.GetType().GetProperty("TargetRaceId")?.GetValue(body)?.ToString()
                ?? body?.GetType().GetProperty("RaceId")?.GetValue(body)?.ToString();

        if (!raceDate.HasValue && !string.IsNullOrWhiteSpace(raceId))
        {
            var race = await queryProcessor.ProcessAsync(
                new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId), request.HttpContext.RequestAborted);
            raceDate = race?.RaceDate;
        }

        var decision = await stateStore.ValidateRaceMutationLeaseAsync(
            raceId, raceDate,
            request.Headers["X-Collection-Job-Id"].FirstOrDefault(),
            request.Headers["X-Collection-Lease-Token"].FirstOrDefault(),
            request.HttpContext.RequestAborted);
        if (decision.Allowed)
            return await next(context);

        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Race collection is in progress.",
            detail: "収集完了後に再試行してください。",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "RaceCollectionInProgress",
                ["jobId"] = decision.ActiveJobId,
                ["leaseExpiresAt"] = decision.LeaseExpiresAt
            });
    }
}
