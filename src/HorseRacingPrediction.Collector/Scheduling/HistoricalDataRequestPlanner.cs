using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.ApiClient;
using Microsoft.Extensions.Logging;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed class HistoricalDataRequestPlanner
{
    private static readonly string JraProviderType = "JRA";

    private readonly IRaceQueryService _raceQueryService;
    private readonly ProcessingStateStore _stateStore;
    private readonly IHistoricalRaceReferenceCollector _historicalRaceReferenceCollector;
    private readonly ILogger<HistoricalDataRequestPlanner> _logger;

    public HistoricalDataRequestPlanner(
        IRaceQueryService raceQueryService,
        ProcessingStateStore stateStore,
        IHistoricalRaceReferenceCollector historicalRaceReferenceCollector,
        ILogger<HistoricalDataRequestPlanner> logger)
    {
        _raceQueryService = raceQueryService;
        _stateStore = stateStore;
        _historicalRaceReferenceCollector = historicalRaceReferenceCollector;
        _logger = logger;
    }

    public async Task<HistoricalDataRequestPlan> EnsureRequestsForRaceAsync(
        string raceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var context = await _raceQueryService.GetRacePredictionContextAsync(raceId, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            _logger.LogDebug("RacePredictionContext が見つからないため過去データ補完要求をスキップします。RaceId={RaceId}", raceId);
            return new HistoricalDataRequestPlan(0, 0, 0);
        }

        var entityPlan = await EnsureEntityHistoryRequestsAsync(context, raceId, now, cancellationToken).ConfigureAwait(false);
        if (entityPlan.RequestedHorseHistoryCount == 0 && entityPlan.RequestedJockeyHistoryCount == 0)
        {
            return entityPlan;
        }

        var raceResultPlan = await EnsureRaceResultRequestsAsync(context, raceId, now, cancellationToken).ConfigureAwait(false);
        return new HistoricalDataRequestPlan(
            entityPlan.RequestedHorseHistoryCount,
            entityPlan.RequestedJockeyHistoryCount,
            raceResultPlan.RequestedRaceResultCount);
    }

    private async Task<HistoricalDataRequestPlan> EnsureEntityHistoryRequestsAsync(
        RacePredictionContextReadModel context,
        string raceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var horseIds = new HashSet<string>(StringComparer.Ordinal);
        var jockeyIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in context.Entries)
        {
            var horseHistory = await _raceQueryService.GetHorseRaceHistoryAsync(entry.HorseId, cancellationToken).ConfigureAwait(false);
            if (horseHistory is null || horseHistory.Entries.Count == 0)
            {
                horseIds.Add(entry.HorseId);
            }

            if (string.IsNullOrWhiteSpace(entry.JockeyId))
            {
                continue;
            }

            var jockeyHistory = await _raceQueryService.GetJockeyRaceHistoryAsync(entry.JockeyId, cancellationToken).ConfigureAwait(false);
            if (jockeyHistory is null || jockeyHistory.Entries.Count == 0)
            {
                jockeyIds.Add(entry.JockeyId);
            }
        }

        foreach (var horseId in horseIds)
        {
            await _stateStore.EnqueueJobAsync(
                AgentJobType.HorseHistoryCollectionRequest,
                AgentJobKeyFactory.BuildHorseHistoryCollectionRequestKey(JraProviderType, horseId),
                AgentJobPayloadSerializer.Serialize(new HorseHistoryCollectionRequestPayload(horseId, raceId, JraProviderType)),
                now,
                priority: 150,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        foreach (var jockeyId in jockeyIds)
        {
            await _stateStore.EnqueueJobAsync(
                AgentJobType.JockeyHistoryCollectionRequest,
                AgentJobKeyFactory.BuildJockeyHistoryCollectionRequestKey(JraProviderType, jockeyId),
                AgentJobPayloadSerializer.Serialize(new JockeyHistoryCollectionRequestPayload(jockeyId, raceId, JraProviderType)),
                now,
                priority: 145,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return new HistoricalDataRequestPlan(horseIds.Count, jockeyIds.Count, 0);
    }

    private async Task<HistoricalDataRequestPlan> EnsureRaceResultRequestsAsync(
        RacePredictionContextReadModel context,
        string raceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (context.RaceDate is null || context.RaceNumber is null || string.IsNullOrWhiteSpace(context.RacecourseCode))
        {
            _logger.LogWarning(
                "レース結果補完要求に必要な開催情報が不足しているためスキップします。RaceId={RaceId}",
                raceId);
            return new HistoricalDataRequestPlan(0, 0, 0);
        }

        IReadOnlyList<HistoricalRaceReference> references;
        try
        {
            references = await _historicalRaceReferenceCollector
                .CollectAsync(context.RaceDate.Value, context.RacecourseCode, context.RaceNumber.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "過去レース参照の抽出に失敗しました。RaceId={RaceId}", raceId);
            return new HistoricalDataRequestPlan(0, 0, 0);
        }

        if (references.Count == 0)
        {
            return new HistoricalDataRequestPlan(0, 0, 0);
        }

        var registeredRaceKeys = await BuildRegisteredRaceKeysAsync(references, cancellationToken).ConfigureAwait(false);
        var requestedRaceResultCount = 0;

        foreach (var reference in references)
        {
            if (registeredRaceKeys.Contains(BuildRaceKey(reference)))
            {
                continue;
            }

            await _stateStore.ScheduleJobAsync(
                AgentJobType.HistoricalRaceResultCollectionRequest,
                AgentJobKeyFactory.BuildHistoricalRaceResultCollectionRequestKey(JraProviderType, reference.RaceDate, reference.Racecourse, reference.RaceNumber),
                AgentJobPayloadSerializer.Serialize(new HistoricalRaceResultCollectionRequestPayload(
                    reference.RaceDate,
                    reference.Racecourse,
                    reference.RaceNumber,
                    raceId,
                    JraProviderType)),
                now,
                priority: 155,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            requestedRaceResultCount++;
        }

        return new HistoricalDataRequestPlan(0, 0, requestedRaceResultCount);
    }

    private async Task<HashSet<string>> BuildRegisteredRaceKeysAsync(
        IReadOnlyList<HistoricalRaceReference> references,
        CancellationToken cancellationToken)
    {
        var registeredKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var date in references.Select(x => x.RaceDate).Distinct())
        {
            var races = await _raceQueryService.SearchRegisteredRacesAsync(date, cancellationToken).ConfigureAwait(false);
            foreach (var race in races)
            {
                if (BuildRaceKey(race) is { } key)
                {
                    registeredKeys.Add(key);
                }
            }
        }

        return registeredKeys;
    }

    private static string BuildRaceKey(HistoricalRaceReference reference)
        => $"{reference.RaceDate:yyyy-MM-dd}|{DeterministicIdGenerator.NormalizeKey(reference.Racecourse)}|{reference.RaceNumber:D2}";

    private static string? BuildRaceKey(RaceSearchSummary summary)
    {
        if (summary.RaceDate is null || summary.RaceNumber is null || string.IsNullOrWhiteSpace(summary.RacecourseCode))
        {
            return null;
        }

        return $"{summary.RaceDate:yyyy-MM-dd}|{DeterministicIdGenerator.NormalizeKey(summary.RacecourseCode)}|{summary.RaceNumber.Value:D2}";
    }
}