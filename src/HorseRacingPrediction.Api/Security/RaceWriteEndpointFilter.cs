using System.Text.Json;
using System.Text.RegularExpressions;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api.Security;

/// <summary>Shared lock boundary for race, prediction, memo, and subject-derived history writers.</summary>
public sealed class RaceWriteEndpointFilter(RaceWriteCoordinator coordinator,
    IDbContextProvider<EventStoreDbContext> provider,
    HorseRacingPrediction.CollectionOperations.CollectionPlatform.CollectionPlatformStore collection) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method)) return await next(context);
        var token = context.HttpContext.RequestAborted;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var keys = await ResolveKeysAsync(context, token);
            await using var held = await coordinator.AcquireAsync(keys, token);
            var checkedKeys = await ResolveKeysAsync(context, token);
            if (!keys.SetEquals(checkedKeys)) continue;
            foreach (var raceId in keys.Where(x => x.StartsWith("race-", StringComparison.Ordinal)))
            {
                var repairHold = await collection.GetRaceRepairHoldAsync(raceId, token);
                if (repairHold is { IsActive: true }) return Results.Conflict(new { code = "RaceRepairHeld", raceId });
                if (repairHold is not null && RequiresAssignmentFence(context))
                {
                    var fingerprint = context.HttpContext.Request.Headers["X-Race-Assignment-Fingerprint"].ToString();
                    var generation = context.HttpContext.Request.Headers["X-Race-Hold-Generation"].ToString();
                    if (fingerprint != await coordinator.AssignmentFingerprintAsync(raceId, token)
                        || !long.TryParse(generation, out var receivedGeneration) || receivedGeneration != repairHold.Generation)
                        return Results.Conflict(new { code = "StaleRaceAssignmentFence", raceId });
                }
                var barrier = await coordinator.ReadBarrierAsync(raceId, token);
                if (barrier is { Verified: false })
                    return Results.Conflict(new { code = "RaceRepairPending", raceId });
                if (barrier is { Verified: true } && context.Arguments.OfType<CreatePredictionTicketRequest>().FirstOrDefault() is { } create
                    && create.EntryAssignmentFingerprint != barrier.Fingerprint)
                    return Results.Conflict(new { code = "StalePredictionAssignment", raceId });
            }
            return await next(context);
        }
        return Results.Conflict(new { code = "RaceWriteScopeChanged" });
    }

    private static bool RequiresAssignmentFence(EndpointFilterInvocationContext context) =>
        context.HttpContext.Request.Path.StartsWithSegments("/api/races")
        || context.HttpContext.Request.Path.StartsWithSegments("/api/admin/races")
        || context.Arguments.Any(x => x is HorseRacingPrediction.Contracts.PrepareHorseHistoryRaceRequest);

    private async Task<HashSet<string>> ResolveKeysAsync(EndpointFilterInvocationContext context, CancellationToken token)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        void AddText(string? text)
        {
            foreach (Match match in Regex.Matches(text ?? "", @"(?:race|horse|jockey|trainer|memo)-[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"))
                keys.Add(match.Value);
        }
        foreach (var route in context.HttpContext.Request.RouteValues.Values) AddText(route?.ToString());
        foreach (var argument in context.Arguments.Where(x => x is not null))
        {
            var type = argument!.GetType();
            if (type.Namespace?.Contains("Contracts", StringComparison.Ordinal) == true)
                AddText(JsonSerializer.Serialize(argument, type));
            if (type.GetProperty("RaceDate")?.GetValue(argument) is DateOnly date
                && type.GetProperty("RacecourseCode")?.GetValue(argument) is string course
                && type.GetProperty("RaceNumber")?.GetValue(argument) is int number)
                keys.Add(DeterministicIdGenerator.BuildRaceId(date, course, number));
            if (argument is HorseRacingPrediction.Contracts.DeclareRaceResultBulkRequest bulk)
                foreach (var entry in bulk.Entries ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(entry.HorseName))
                        keys.Add(DeterministicIdGenerator.BuildHorseId(HorseRacingPrediction.Contracts.JraSubjectNameNormalizer.CanonicalizeDisplayName("Horse", entry.HorseName), entry.HorseSourceIdentity));
                    if (!string.IsNullOrWhiteSpace(entry.JockeyName))
                        keys.Add(DeterministicIdGenerator.BuildEntityId("jockey", DeterministicIdGenerator.NormalizeKey(HorseRacingPrediction.Contracts.JraSubjectNameNormalizer.CanonicalizeDisplayName("Jockey", entry.JockeyName))));
                    if (!string.IsNullOrWhiteSpace(entry.TrainerName))
                        keys.Add(DeterministicIdGenerator.BuildEntityId("trainer", DeterministicIdGenerator.NormalizeKey(HorseRacingPrediction.Contracts.JraSubjectNameNormalizer.CanonicalizeDisplayName("Trainer", entry.TrainerName))));
                }
            if (argument is HorseRacingPrediction.Contracts.PrepareHorseHistoryRaceRequest history)
            {
                string[] english = ["Sapporo", "Hakodate", "Fukushima", "Niigata", "Tokyo", "Nakayama", "Chukyo", "Kyoto", "Hanshin", "Kokura"];
                string[] japanese = ["札幌", "函館", "福島", "新潟", "東京", "中山", "中京", "京都", "阪神", "小倉"];
                var index = Array.FindIndex(english, x => x.Equals(history.Course, StringComparison.OrdinalIgnoreCase));
                keys.Add(DeterministicIdGenerator.BuildRaceId(history.RaceDate, index < 0 ? history.Course : japanese[index], history.RaceNumber));
            }
        }
        using var db = provider.CreateContext();
        foreach (var repair in context.Arguments.OfType<ApplyHorseIdentityRepairRequest>())
            foreach (var candidate in await db.HorseIdentityRepairCandidates.AsNoTracking()
                .Where(x => repair.CandidateIds.Contains(x.CandidateId)).ToListAsync(token))
            {
                AddText(candidate.RaceId);
                AddText(candidate.SourceHorseId);
                AddText(candidate.TargetHorseId);
            }
        if (context.HttpContext.Request.RouteValues["predictionTicketId"]?.ToString() is { } predictionId)
        {
            var ticket = await db.PredictionTickets.AsNoTracking().SingleOrDefaultAsync(x => x.PredictionTicketId == predictionId, token);
            if (!string.IsNullOrWhiteSpace(ticket?.RaceId)) keys.Add(ticket.RaceId);
        }
        if (context.HttpContext.Request.RouteValues["memoId"]?.ToString() is { } memoId)
        {
            keys.Add("memo-id:" + memoId);
            foreach (var subject in await db.MemoSubjects.AsNoTracking().ToListAsync(token))
                foreach (var memo in subject.Memos.Where(x => x.MemoId == memoId)) AddText(JsonSerializer.Serialize(memo));
        }
        foreach (var memo in context.Arguments.OfType<CreateMemoRequest>())
            if (memo.MemoId is not null) keys.Add("memo-id:" + memo.MemoId);
        foreach (var raceId in keys.Where(x => x.StartsWith("race-", StringComparison.Ordinal)).ToArray())
        {
            var race = await db.RacePredictionContexts.AsNoTracking().SingleOrDefaultAsync(x => x.RaceId == raceId, token);
            foreach (var entry in race?.Entries ?? [])
            {
                keys.Add(entry.HorseId);
                if (entry.JockeyId is not null) keys.Add(entry.JockeyId);
                if (entry.TrainerId is not null) keys.Add(entry.TrainerId);
            }
        }
        return keys;
    }
}
