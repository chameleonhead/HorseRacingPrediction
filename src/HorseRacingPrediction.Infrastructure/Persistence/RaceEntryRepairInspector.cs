using System.Reflection;
using System.Text.Json;
using EventFlow.Aggregates;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.EventStores;
using EventFlow.EventStores;
using EventFlow.ReadStores;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Memos;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Infrastructure.Persistence;

/// <summary>Read-only event census and independent projection replay before an assignment repair.</summary>
public sealed class RaceEntryRepairInspector(IDbContextProvider<EventStoreDbContext> provider, IEventStore events,
    IAggregateStore aggregates)
{
    private static readonly string MemoAggregateName = new MemoAggregate(MemoId.New).Name.Value;
    private static readonly HashSet<string> KnownTables =
    [
        "EventEntity", "SnapshotEntity", "Horses", "Jockeys", "Trainers", "RacePredictionContexts", "RaceResults",
        "PredictionTickets", "HorseWeightHistories", "PredictionComparisons", "MemoSubjects", "HorseRaceHistories",
        "JockeyRaceHistories", "RaceSummaries", "OwnerAliasMappings", "OwnerMergeAudits", "HorseIdentityRepairCandidates",
        "HorseIdentityRepairRedirects", "SubjectIdentificationRepairIssues", "JraSubjectProfileReadModel",
        "__EFMigrationsHistory", "__EFMigrationsLock", "sqlite_sequence"
    ];
    private static readonly HashSet<Type> EntryOnlyEvents =
    [
        typeof(RaceCreated), typeof(RaceCardPublished), typeof(EntryRegistered), typeof(EntryCollectedDataUpdated),
        typeof(RaceDataCorrected), typeof(RaceWeatherObserved), typeof(RaceTrackConditionObserved),
        typeof(RaceLifecycleStatusChanged), typeof(RaceStarted), typeof(RaceEntryAssignmentsRepaired)
    ];

    public async Task<RaceRepairInspection> InspectAsync(string raceId, CancellationToken token)
    {
        using var db = provider.CreateContext();
        var stream = await events.LoadEventsAsync<RaceAggregate, RaceId>(new RaceId(raceId), token);
        if (stream.Count == 0) throw new ArgumentException("Race not found.");
        var aggregate = new RaceAggregate(new RaceId(raceId));
        aggregate.ApplyEvents(stream.Cast<IDomainEvent>().ToArray());
        var details = aggregate.GetDetails();
        var blockers = new List<string>();
        await db.Database.OpenConnectionAsync(token);
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                if (!KnownTables.Contains(reader.GetString(0))) blockers.Add("UnknownSchemaTable:" + reader.GetString(0));
        }
        var loaded = await aggregates.LoadAsync<RaceAggregate, RaceId>(new RaceId(raceId), token);
        Compare("AggregateSnapshot", details, loaded.GetDetails(), blockers);
        if (loaded.Version != aggregate.Version) blockers.Add("AggregateSnapshotVersionMismatch");
        var counts = new Dictionary<string, int>();
        var actualContext = await db.RacePredictionContexts.AsNoTracking().SingleOrDefaultAsync(x => x.RaceId == raceId, token);
        foreach (var item in stream)
            if (!EntryOnlyEvents.Contains(item.EventType)) blockers.Add("IndependentOrUnknownRaceEvent:" + item.EventType.Name);
        if (stream.Count != aggregate.Version || stream.Max(x => x.AggregateSequenceNumber) != aggregate.Version)
            blockers.Add("RaceEventVersionMismatch");

        // Query authoritative events, including withdrawn/deleted objects, not just current UI projections.
        var relatedIds = await db.Set<EventEntity>().AsNoTracking()
            .Where(x => x.AggregateId != raceId && x.Data.Contains(raceId))
            .Select(x => x.AggregateId).Distinct().ToListAsync(token);
        var horseIds = details.Entries.Select(x => x.HorseId).Distinct().ToArray();
        foreach (var horseId in horseIds)
            relatedIds.AddRange(await db.Set<EventEntity>().AsNoTracking()
                .Where(x => x.AggregateName == MemoAggregateName && x.Data.Contains(horseId))
                .Select(x => x.AggregateId).Distinct().ToListAsync(token));
        var memoIds = await db.Set<EventEntity>().Where(x => x.AggregateName == MemoAggregateName)
            .Select(x => x.AggregateId).Distinct().ToListAsync(token);
        foreach (var id in relatedIds.Distinct())
        {
            if (memoIds.Contains(id))
            {
                var memoEvents = await events.LoadEventsAsync<MemoAggregate, MemoId>(new MemoId(id), token);
                // Only unchanged, generated race-level source citations have no horse-number meaning.
                if (memoEvents.Count == 1 && memoEvents.Single().GetAggregateEvent() is MemoCreated citation
                    && IsRaceSourceCitation(citation, raceId)) continue;
                blockers.Add("MemoReference:" + id);
            }
            else blockers.Add("IndependentOrUnknownReference:" + id);
        }
        counts["relatedEventAggregates"] = relatedIds.Distinct().Count();
        counts["predictionTickets"] = await db.PredictionTickets.CountAsync(x => x.RaceId == raceId, token);
        if (counts["predictionTickets"] != 0) blockers.Add("PredictionTickets");
        counts["identityRepairCandidates"] = await db.HorseIdentityRepairCandidates.CountAsync(x => x.RaceId == raceId
            || horseIds.Contains(x.SourceHorseId) || horseIds.Contains(x.TargetHorseId), token);
        if (counts["identityRepairCandidates"] != 0) blockers.Add("IdentityRepairCandidates");
        counts["subjectIdentificationIssues"] = await db.SubjectIdentificationRepairIssues.CountAsync(x =>
            x.RequestedByRaceId == raceId || horseIds.Contains(x.SubjectId) || horseIds.Contains(x.TargetSubjectId!), token);
        if (counts["subjectIdentificationIssues"] != 0) blockers.Add("SubjectIdentificationRepairIssues");
        if (await db.HorseIdentityRepairRedirects.AnyAsync(x => horseIds.Contains(x.SourceHorseId)
            || horseIds.Contains(x.TargetHorseId), token)) blockers.Add("IdentityRepairRedirects");

        var expectedContext = await ReplayAsync(new RacePredictionContextReadModel(), stream, token);
        var expectedResults = await ReplayAsync(new RaceResultViewReadModel(), stream, token);
        var expectedComparison = await ReplayAsync(new PredictionComparisonViewReadModel(), stream, token);
        var actualResults = await db.RaceResults.AsNoTracking().SingleOrDefaultAsync(x => x.RaceId == raceId, token);
        var actualComparison = await db.PredictionComparisons.AsNoTracking().SingleOrDefaultAsync(x => x.RaceId == raceId, token);
        Compare("RaceContext", expectedContext, actualContext, blockers);
        Compare("ResultIndexes", expectedResults.EntryIndexes, actualResults?.EntryIndexes, blockers);
        Compare("ResultProjection", expectedResults, actualResults, blockers);
        Compare("ComparisonIndexes", expectedComparison.EntryIndexes, actualComparison?.EntryIndexes, blockers);
        Compare("ComparisonProjection", expectedComparison, actualComparison, blockers);
        var expectedSummary = await ReplayAsync(new RaceSummaryReadModel(), stream, token);
        Compare("RaceSummary", expectedSummary,
            await db.RaceSummaries.AsNoTracking().SingleOrDefaultAsync(x => x.RaceId == raceId, token), blockers);

        // Inspect every stored history row for this race, including stale rows under an unexpected subject.
        var horseHistories = await db.HorseRaceHistories.AsNoTracking().ToListAsync(token);
        var jockeyHistories = await db.JockeyRaceHistories.AsNoTracking().ToListAsync(token);
        var weights = await db.HorseWeightHistories.AsNoTracking().ToListAsync(token);
        var horseLocator = new HorseRaceHistoryLocator();
        var jockeyLocator = new JockeyRaceHistoryLocator();
        var weightLocator = new HorseWeightHistoryLocator();
        var expectedHorses = new Dictionary<string, HorseRaceHistoryReadModel>();
        var expectedJockeys = new Dictionary<string, JockeyRaceHistoryReadModel>();
        var expectedWeights = new Dictionary<string, HorseWeightHistoryReadModel>();
        foreach (var item in stream.OrderBy(x => x.AggregateSequenceNumber))
        {
            await DistributeAsync(expectedHorses, horseLocator.GetReadModelIds(item), item, token);
            await DistributeAsync(expectedJockeys, jockeyLocator.GetReadModelIds(item), item, token);
            await DistributeAsync(expectedWeights, weightLocator.GetReadModelIds(item), item, token);
        }
        foreach (var id in expectedHorses.Keys.Concat(horseHistories.Where(x => x.Entries.Any(e => e.RaceId == raceId)).Select(x => x.HorseId)).Distinct())
            Compare("HorseHistory:" + id, expectedHorses.GetValueOrDefault(id)?.Entries ?? [],
                horseHistories.SingleOrDefault(x => x.HorseId == id)?.Entries.Where(x => x.RaceId == raceId).ToList() ?? [], blockers);
        foreach (var id in expectedJockeys.Keys.Concat(jockeyHistories.Where(x => x.Entries.Any(e => e.RaceId == raceId)).Select(x => x.JockeyId)).Distinct())
            Compare("JockeyHistory:" + id, expectedJockeys.GetValueOrDefault(id)?.Entries ?? [],
                jockeyHistories.SingleOrDefault(x => x.JockeyId == id)?.Entries.Where(x => x.RaceId == raceId).ToList() ?? [], blockers);
        foreach (var id in expectedWeights.Keys.Concat(weights.Where(x => x.WeightHistory.Any(e => e.RaceId == raceId)).Select(x => x.HorseId)).Distinct())
            Compare("WeightHistory:" + id, expectedWeights.GetValueOrDefault(id)?.WeightHistory ?? [],
                weights.SingleOrDefault(x => x.HorseId == id)?.WeightHistory.Where(x => x.RaceId == raceId).ToList() ?? [], blockers);
        var finalVersion = await db.Set<EventEntity>().Where(x => x.AggregateId == raceId)
            .MaxAsync(x => x.AggregateSequenceNumber, token);
        if (finalVersion != aggregate.Version) blockers.Add("RaceChangedDuringInspection");
        counts["raceEvents"] = stream.Count;
        counts["oddsSnapshots"] = actualContext?.OddsSnapshots.Count ?? 0;
        var repair = stream.Select(x => x.GetAggregateEvent()).OfType<RaceEntryAssignmentsRepaired>().SingleOrDefault();
        return new(details, aggregate.Version, blockers.Distinct().ToArray(), counts, repair);
    }

    private static bool IsRaceSourceCitation(MemoCreated memo, string raceId)
        => memo.AuthorId == "Collector" && memo.MemoType == "SourceCitation"
            && memo.Content is "JRA出馬表" or "JRAレース結果"
            && memo.Subjects.Count == 1 && memo.Subjects[0].SubjectType == MemoSubjectType.Race
            && memo.Subjects[0].SubjectId == raceId && memo.Links.Count == 1
            && memo.Links[0].Title == memo.Content && memo.Links[0].LinkType == MemoLinkType.Url
            && Uri.TryCreate(memo.Links[0].Url, UriKind.Absolute, out var url)
            && url.Scheme == "https" && url.Host is "www.jra.go.jp" or "www.jra.jp"
            && url.AbsolutePath is "/JRADB/accessD.html" or "/JRADB/accessS.html";

    private static void Compare<T>(string name, T expected, T actual, List<string> blockers)
    {
        if (JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(actual)) blockers.Add("ProjectionMismatch:" + name);
    }

    private static async Task<T> ReplayAsync<T>(T model, IEnumerable<IDomainEvent> stream, CancellationToken token) where T : IReadModel
    {
        foreach (var item in stream.OrderBy(x => x.AggregateSequenceNumber)) await ApplyAsync(model, item, token);
        return model;
    }

    private static async Task DistributeAsync<T>(Dictionary<string, T> models, IEnumerable<string> ids,
        IDomainEvent item, CancellationToken token) where T : IReadModel, new()
    {
        foreach (var id in ids.Distinct())
        {
            if (!models.TryGetValue(id, out var model)) models[id] = model = new T();
            await ApplyAsync(model, item, token);
        }
    }

    private static async Task ApplyAsync(IReadModel model, IDomainEvent item, CancellationToken token)
    {
        var method = model.GetType().GetMethods().SingleOrDefault(m => m.Name == "ApplyAsync"
            && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType.IsInstanceOfType(item));
        if (method is not null) await (Task)method.Invoke(model, [null, item, token])!;
    }
}

public sealed record RaceRepairInspection(RaceDetails Race, int Version, IReadOnlyList<string> Blockers,
    IReadOnlyDictionary<string, int> ReferenceCounts, RaceEntryAssignmentsRepaired? PreviousRepair);
