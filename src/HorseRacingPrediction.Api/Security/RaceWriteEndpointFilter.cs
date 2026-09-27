using EventFlow.EntityFramework;
using EventFlow.EventStores;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HorseRacingPrediction.Api.Security;

/// <summary>Shared lock boundary for race, prediction, memo, and subject-derived history writers.</summary>
public sealed class RaceWriteEndpointFilter(RaceWriteCoordinator coordinator,
    IDbContextProvider<EventStoreDbContext> provider,
    HorseRacingPrediction.CollectionOperations.CollectionPlatform.CollectionPlatformStore collection,
    IEventStore events) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method)) return await next(context);
        var token = context.HttpContext.RequestAborted;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            HashSet<string> keys;
            try { keys = await ResolveKeysAsync(context, token); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { code = ex.Message }); }
            await using var held = await coordinator.AcquireAsync(keys, token);
            HashSet<string> checkedKeys;
            try { checkedKeys = await ResolveKeysAsync(context, token); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { code = ex.Message }); }
            if (!keys.SetEquals(checkedKeys)) continue;
            foreach (var raceId in keys.Where(x => x.StartsWith("race-", StringComparison.Ordinal)))
            {
                var repairHold = await collection.GetRaceRepairHoldAsync(raceId, token);
                if (repairHold is { IsActive: true }) return Results.Conflict(new { code = "RaceRepairHeld", raceId });
                if ((repairHold is not null && RequiresAssignmentFence(context))
                    || context.HttpContext.Request.Path.Value?.EndsWith("/odds-snapshots", StringComparison.Ordinal) == true)
                {
                    var fingerprint = context.HttpContext.Request.Headers["X-Race-Assignment-Fingerprint"].ToString();
                    var generation = context.HttpContext.Request.Headers["X-Race-Hold-Generation"].ToString();
                    if (fingerprint != await coordinator.AssignmentFingerprintAsync(raceId, token)
                        || !long.TryParse(generation, out var receivedGeneration) || receivedGeneration != (repairHold?.Generation ?? 0))
                        return Results.Conflict(new { code = "StaleRaceAssignmentFence", raceId });
                }
                var barrier = await coordinator.ReadBarrierAsync(raceId, token);
                if (barrier is { Verified: false })
                    return Results.Conflict(new { code = "RaceRepairPending", raceId });
                if (context.Arguments.OfType<CreatePredictionTicketRequest>().FirstOrDefault() is { } create
                    && (barrier is { Verified: true } || !string.IsNullOrWhiteSpace(create.EntryAssignmentFingerprint))
                    && create.EntryAssignmentFingerprint != await coordinator.AssignmentFingerprintAsync(raceId, token))
                    return Results.Conflict(new { code = "StalePredictionAssignment", raceId });
            }
            if (await ValidatePredictionParticipationAsync(context, token) is { } participationError)
                return participationError;
            return await next(context);
        }
        return Results.Conflict(new { code = "RaceWriteScopeChanged" });
    }

    private async Task<IResult?> ValidatePredictionParticipationAsync(EndpointFilterInvocationContext context, CancellationToken token)
    {
        var mark = context.Arguments.OfType<AddPredictionMarkRequest>().FirstOrDefault();
        var finalize = context.HttpContext.Request.Path.Value?.EndsWith("/finalize", StringComparison.Ordinal) == true;
        if (mark is null && !finalize) return null;
        if (context.HttpContext.Request.RouteValues["predictionTicketId"]?.ToString() is not { } ticketId) return null;
        using var db = provider.CreateContext();
        var ticket = await db.PredictionTickets.AsNoTracking().SingleOrDefaultAsync(x => x.PredictionTicketId == ticketId, token);
        if (ticket is null) return null;
        var history = await events.LoadEventsAsync<PredictionTicketAggregate, PredictionTicketId>(new(ticketId), token);
        var fingerprint = history.Select(x => x.GetAggregateEvent()).OfType<PredictionTicketCreated>()
            .SingleOrDefault()?.EntryAssignmentFingerprint;
        if (fingerprint is not null && fingerprint != await coordinator.AssignmentFingerprintAsync(ticket.RaceId!, token))
            return Results.Conflict(new { code = "StalePredictionAssignment", raceId = ticket.RaceId });
        var race = await db.RacePredictionContexts.AsNoTracking().SingleOrDefaultAsync(x => x.RaceId == ticket.RaceId, token);
        var selectedIds = mark is null ? ticket.Marks.Select(x => x.EntryId) : [mark.EntryId];
        if (selectedIds.Any(id => race?.Entries.Any(entry => entry.EntryId == id
                && entry.ParticipationStatus == HorseRacingPrediction.Domain.Races.RaceEntryParticipationStatus.Active) != true))
            return Results.Conflict(new { code = "PredictionEntryNotActive", raceId = ticket.RaceId });
        return null;
    }

    private static bool RequiresAssignmentFence(EndpointFilterInvocationContext context) =>
        context.HttpContext.Request.Path.StartsWithSegments("/api/races")
        || context.HttpContext.Request.Path.StartsWithSegments("/api/admin/races")
        || context.Arguments.Any(x => x is PrepareHorseHistoryRaceRequest);

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
            if (argument is DeclareRaceResultBulkRequest bulk)
                foreach (var entry in bulk.Entries ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(entry.HorseName))
                        keys.Add(DeterministicIdGenerator.BuildHorseId(JraSubjectNameNormalizer.CanonicalizeDisplayName("Horse", entry.HorseName), entry.HorseSourceIdentity));
                    if (!string.IsNullOrWhiteSpace(entry.JockeyName))
                        keys.Add(DeterministicIdGenerator.BuildEntityId("jockey", DeterministicIdGenerator.NormalizeKey(JraSubjectNameNormalizer.CanonicalizeDisplayName("Jockey", entry.JockeyName))));
                    if (!string.IsNullOrWhiteSpace(entry.TrainerName))
                        keys.Add(DeterministicIdGenerator.BuildEntityId("trainer", DeterministicIdGenerator.NormalizeKey(JraSubjectNameNormalizer.CanonicalizeDisplayName("Trainer", entry.TrainerName))));
                }
            if (argument is PrepareHorseHistoryRaceRequest history)
            {
                keys.Add(DeterministicIdGenerator.BuildRaceId(history.RaceDate, history.Course, history.RaceNumber));
            }
        }
        using var db = provider.CreateContext();
        // Canonical scope locks serialize alternate spellings before their persisted IDs are resolved.
        keys.Add("identity-resolution");
        var horseIdentities = context.Arguments.OfType<DeclareRaceResultBulkRequest>().Any()
            ? await CollectionIdentityResolver.LoadHorsesAsync(db, token) : [];
        foreach (var argument in context.Arguments.Where(x => x is not null))
        {
            var type = argument!.GetType();
            if (type.GetProperty("RaceDate")?.GetValue(argument) is DateOnly date
                && type.GetProperty("RacecourseCode")?.GetValue(argument) is string course
                && type.GetProperty("RaceNumber")?.GetValue(argument) is int number
                && string.IsNullOrWhiteSpace(type.GetProperty("RaceId")?.GetValue(argument)?.ToString())
                && string.IsNullOrWhiteSpace(type.GetProperty("TargetRaceId")?.GetValue(argument)?.ToString()))
                keys.Add(await CollectionIdentityResolver.RaceAsync(db, date, course, number, token));
            if (argument is DeclareRaceResultBulkRequest bulk)
                foreach (var entry in bulk.Entries ?? [])
                    if (!string.IsNullOrWhiteSpace(entry.HorseName)
                        && (string.IsNullOrWhiteSpace(entry.HorseSourceIdentity) || JraSourceIdentity.TryNormalizeHorse(entry.HorseSourceIdentity, out _)))
                    {
                        try { keys.Add(CollectionIdentityResolver.ResolveHorse(horseIdentities, entry.HorseName, entry.HorseSourceIdentity, null)); }
                        // The endpoint returns a structured preflight rejection under the identity lock.
                        catch (InvalidOperationException) { }
                    }
        }
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
