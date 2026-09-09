using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using Microsoft.Extensions.Logging;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed partial class CollectionExecutionService
{
    private async Task ExecuteRaceDayReacquisitionAsync(
        LeasedCollectionTask task,
        DateTimeOffset now,
        CancellationToken token)
    {
        var payload = AgentJobPayloadSerializer.Deserialize<RaceDayReacquisitionPayload>(task.Payload);
        if (!string.Equals(payload.ProviderType, JraProviderType, StringComparison.Ordinal))
            throw new InvalidOperationException($"未対応のProviderです: {payload.ProviderType}");

        var discovered = await DiscoverOfficialRaceDayAsync(payload.RaceDate, token).ConfigureAwait(false);
        if (discovered.Count == 0)
        {
            _logger.LogInformation("[開催日再取得] JRA公式日程に開催レースがないため正常終了します。Date={Date}", payload.RaceDate);
            await _stateStore.CompleteCollectionTaskAsync(
                task.JobType, task.DeduplicationKey, task.LeaseToken, token).ConfigureAwait(false);
            return;
        }

        // DBは対象集合の正本にはしない。公式一覧で発見したレースについて、既存IDを
        // 維持するための照合にだけ使用し、未登録なら決定論的IDで新規保存可能にする。
        var registered = await _raceQueryService.SearchRegisteredRacesAsync(payload.RaceDate, token).ConfigureAwait(false);
        var registeredIds = registered
            .Where(x => x.RaceDate == payload.RaceDate && x.RaceNumber is >= 1 and <= 12)
            .Select(x => new { Race = x, Course = RaceReacquisitionCourseResolver.Resolve(x.RacecourseCode) })
            .Where(x => x.Course is not null && !string.IsNullOrWhiteSpace(x.Race.RaceId))
            .GroupBy(x => RaceDataCollectionKeyFactory.Build(payload.RaceDate, x.Course!, x.Race.RaceNumber!.Value))
            .ToDictionary(x => x.Key, x => x.First().Race.RaceId, StringComparer.Ordinal);

        foreach (var race in discovered)
        {
            var racecourse = RaceCourseNames.GetJraName(race.Course);
            var identityKey = RaceDataCollectionKeyFactory.Build(race.Date, racecourse, race.Number);
            var targetRaceId = registeredIds.GetValueOrDefault(identityKey)
                ?? DeterministicIdGenerator.BuildRaceId(race.Date, racecourse, race.Number);
            var child = new RaceReacquisitionPayload(
                targetRaceId,
                race.Date,
                racecourse,
                race.Number);
            await _stateStore.ScheduleJobAsync(
                AgentJobType.RaceReacquisition,
                $"{race}:{task.TaskId}",
                AgentJobPayloadSerializer.Serialize(child),
                now,
                priority: 230,
                parentJobId: task.TaskId,
                parentRelationType: JobRelationType.AggregatedBy,
                cancellationToken: token).ConfigureAwait(false);
        }

        _logger.LogInformation("[開催日再取得] JRA公式一覧から子ジョブを登録しました。Date={Date} Count={Count}", payload.RaceDate, discovered.Count);
        await _stateStore.WaitForCollectionDependenciesAsync(
            task.JobType, task.DeduplicationKey, task.LeaseToken, token).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<RaceId>> DiscoverOfficialRaceDayAsync(DateOnly raceDate, CancellationToken token)
    {
        await using var session = await _sessionFactory.CreateAsync(token).ConfigureAwait(false);
        var courses = await _scheduleWorkflowFactory(session).CollectAsync(raceDate, token).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Jst).Date);
        var includeCard = session.Navigate.IsWithinRaceCardLookupPeriod(raceDate);
        var races = new HashSet<RaceId>();

        foreach (var course in courses.Where(x => x != RaceCourse.Unknown).Distinct())
        {
            token.ThrowIfCancellationRequested();
            if (includeCard)
            {
                var cardList = await session.Navigate.ToRaceListAsync(raceDate, course, token).ConfigureAwait(false);
                AddRaces(cardList, races);
            }

            if (raceDate <= today)
            {
                try
                {
                    var resultList = await session.Navigate.ToRaceResultListAsync(raceDate, course, token).ConfigureAwait(false);
                    AddRaces(resultList, races);
                }
                catch (JraNavigationException ex) when (ex.Reason == JraNavigationFailureReason.NotYetPublished && includeCard)
                {
                    // 当日など、出馬表は公開済みでも結果一覧がまだない状態は正常。
                }
            }
        }

        return races.OrderBy(x => x.Course).ThenBy(x => x.Number).ToArray();
    }

    private static void AddRaces(IJraPage page, HashSet<RaceId> races)
    {
        switch (page)
        {
            case JraRaceListPage list:
                foreach (var race in list.Races.Where(x => x.Number is >= 1 and <= 12))
                    races.Add(race.Id);
                break;
            case JraRaceResultPage result:
                races.Add(result.RaceId);
                break;
            default:
                throw new InvalidOperationException($"公式レース一覧を取得できませんでした。Kind={page.Kind}");
        }
    }
}

internal static class RaceReacquisitionCourseResolver
{
    public static string? Resolve(string? value) => value?.ToUpperInvariant() switch
    {
        "SAPPORO" or "札幌" => "札幌",
        "HAKODATE" or "函館" => "函館",
        "FUKUSHIMA" or "福島" => "福島",
        "NIIGATA" or "新潟" => "新潟",
        "TOKYO" or "東京" => "東京",
        "NAKAYAMA" or "中山" => "中山",
        "CHUKYO" or "中京" => "中京",
        "KYOTO" or "京都" => "京都",
        "HANSHIN" or "阪神" => "阪神",
        "KOKURA" or "小倉" => "小倉",
        _ => null,
    };
}
