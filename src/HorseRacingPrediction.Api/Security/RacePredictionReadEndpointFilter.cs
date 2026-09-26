using EventFlow.EntityFramework;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api.Security;

/// <summary>Prevent shared histories from exposing a partially projected assignment repair.</summary>
public sealed class RacePredictionReadEndpointFilter(RaceWriteCoordinator coordinator,
    IDbContextProvider<EventStoreDbContext> provider) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var token = context.HttpContext.RequestAborted;
        var route = context.HttpContext.Request.RouteValues;
        var raceId = route["raceId"]?.ToString();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var keys = await KeysAsync();
            await using var held = await coordinator.AcquireAsync(keys, token);
            if (!keys.SetEquals(await KeysAsync())) continue;
            using var db = provider.CreateContext();
            var related = new HashSet<string>(StringComparer.Ordinal);
            if (raceId is not null) related.Add(raceId);
            foreach (var model in await db.HorseRaceHistories.AsNoTracking().Where(x => keys.Contains(x.HorseId)).ToListAsync(token))
                related.UnionWith(model.Entries.Select(x => x.RaceId));
            foreach (var model in await db.JockeyRaceHistories.AsNoTracking().Where(x => keys.Contains(x.JockeyId)).ToListAsync(token))
                related.UnionWith(model.Entries.Select(x => x.RaceId));
            foreach (var model in await db.HorseWeightHistories.AsNoTracking().Where(x => keys.Contains(x.HorseId)).ToListAsync(token))
                related.UnionWith(model.WeightHistory.Select(x => x.RaceId));
            // An event may be committed before any subject history row was projected.
            foreach (var subject in keys.Where(x => x.StartsWith("horse-", StringComparison.Ordinal) || x.StartsWith("jockey-", StringComparison.Ordinal)))
                related.UnionWith(await db.Set<EventFlow.EntityFramework.EventStores.EventEntity>().AsNoTracking()
                    .Where(x => x.Data.Contains(subject) && x.Data.ToLower().Contains("\"fingerprint\""))
                    .Select(x => x.AggregateId).Distinct().ToListAsync(token));
            foreach (var id in related)
                if (await coordinator.ReadBarrierAsync(id, token) is { Verified: false })
                    return Results.Conflict(new { code = "RaceRepairPending", raceId = id });
            return await next(context);
        }
        return Results.Conflict(new { code = "RaceReadScopeChanged" });

        async Task<HashSet<string>> KeysAsync()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in new[] { "raceId", "horseId", "jockeyId" })
                if (route[name]?.ToString() is { } id) keys.Add(id);
            if (raceId is not null)
            {
                using var db = provider.CreateContext();
                var race = await db.RacePredictionContexts.AsNoTracking().SingleOrDefaultAsync(x => x.RaceId == raceId, token);
                foreach (var entry in race?.Entries ?? [])
                {
                    keys.Add(entry.HorseId);
                    if (entry.JockeyId is not null) keys.Add(entry.JockeyId);
                }
            }
            return keys;
        }
    }
}
