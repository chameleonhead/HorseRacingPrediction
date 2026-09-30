using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Security;

public sealed class RaceActiveCollectionEndpointFilter(CollectionPlatformStore store,
    EventFlow.EntityFramework.IDbContextProvider<HorseRacingPrediction.Infrastructure.Persistence.EventStoreDbContext> provider) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        string? raceId;
        try { raceId = await ResolveRaceIdAsync(context); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { code = ex.Message }); }
        if (raceId is null) return await next(context).ConfigureAwait(false);
        var hasLease = context.HttpContext.Request.Headers.ContainsKey("X-Collection-Task-Id")
            || context.HttpContext.Request.Headers.ContainsKey("X-Collection-Lease-Token");
        if (hasLease)
        {
            if (Guid.TryParse(context.HttpContext.Request.Headers["X-Collection-Task-Id"], out var taskId)
            && context.HttpContext.Request.Headers.TryGetValue("X-Collection-Lease-Token", out var leaseToken)
            && await store.IsValidActiveRaceLeaseAsync(taskId, leaseToken.ToString(), raceId,
                context.HttpContext.RequestAborted).ConfigureAwait(false))
                return await next(context).ConfigureAwait(false);
            return Results.Conflict(new { code = "InvalidCollectionLease", message = "有効な収集leaseを確認できません。" });
        }
        if (await store.HasActiveRaceMutationAsync(raceId,
                context.HttpContext.RequestAborted).ConfigureAwait(false))
            return Results.Conflict(new { message = "収集処理中のためレース情報を変更できません。" });
        return await next(context).ConfigureAwait(false);
    }

    private async Task<string?> ResolveRaceIdAsync(EndpointFilterInvocationContext context)
    {
        var routeId = context.HttpContext.Request.RouteValues["raceId"]?.ToString();
        if (!string.IsNullOrWhiteSpace(routeId)) return routeId;
        foreach (var argument in context.Arguments.Where(x => x is not null))
        {
            var contractInput = GetContractInput(argument!);
            var type = contractInput?.GetType() ?? argument!.GetType();
            var target = type.GetProperty("TargetRaceId")?.GetValue(contractInput)?.ToString();
            if (!string.IsNullOrWhiteSpace(target)) return target;
            var explicitRaceId = type.GetProperty("RaceId")?.GetValue(contractInput)?.ToString();
            if (!string.IsNullOrWhiteSpace(explicitRaceId)) return explicitRaceId;
            if (type.GetProperty("RaceDate")?.GetValue(contractInput) is DateOnly date
                && type.GetProperty("RacecourseCode")?.GetValue(contractInput) is string course
                && type.GetProperty("RaceNumber")?.GetValue(contractInput) is int number)
            {
                using var db = provider.CreateContext();
                return await CollectionIdentityResolver.RaceAsync(db, date, course, number, context.HttpContext.RequestAborted);
            }
        }
        return null;
    }

    private static object? GetContractInput(object argument) => argument switch
    {
        CreateRaceRequest { Race: { } input } => input,
        CreateRaceFromScheduleRequest { Schedule: { } input } => input,
        CorrectRaceDataRequest { Race: { } input } => input,
        RegisterEntryRequest { Entry: { } input } => input,
        UpdateEntryCollectedDataRequest { Entry: { } input } => input,
        DeclareEntryResultRequest { Result: { } input } => input,
        DeclareRaceResultRequest { Result: { } input } => input,
        DeclarePayoutResultRequest { Payout: { } input } => input,
        DeclareRaceResultBulkRequest { Result: { } input } => input,
        MarkRaceRescheduledRequest { Reschedule: { } input } => input,
        PublishRaceCardRequest { Card: { } input } => input,
        RecordWeatherObservationRequest { Observation: { } input } => input,
        RecordTrackConditionRequest { Observation: { } input } => input,
        _ => argument
    };
}
