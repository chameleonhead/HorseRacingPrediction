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

        var races = await _raceQueryService.SearchRegisteredRacesAsync(payload.RaceDate, token).ConfigureAwait(false);
        var targets = races
            .Where(x => x.RaceDate == payload.RaceDate && x.RaceNumber is >= 1 and <= 12)
            .Select(x => new
            {
                Race = x,
                Course = RaceReacquisitionCourseResolver.Resolve(x.RacecourseCode),
            })
            .Where(x => x.Course is not null && !string.IsNullOrWhiteSpace(x.Race.RaceId))
            .DistinctBy(x => x.Race.RaceId)
            .ToList();

        if (targets.Count == 0)
        {
            _logger.LogInformation("[開催日再取得] 登録済みJRAレースがないため正常終了します。Date={Date}", payload.RaceDate);
            await _stateStore.CompleteCollectionTaskAsync(
                task.JobType, task.DeduplicationKey, task.LeaseToken, token).ConfigureAwait(false);
            return;
        }

        foreach (var target in targets)
        {
            var child = new RaceReacquisitionPayload(
                target.Race.RaceId,
                payload.RaceDate,
                target.Course!,
                target.Race.RaceNumber!.Value);
            await _stateStore.ScheduleJobAsync(
                AgentJobType.RaceReacquisition,
                $"{target.Race.RaceId}:{task.TaskId}",
                AgentJobPayloadSerializer.Serialize(child),
                now,
                priority: 230,
                parentJobId: task.TaskId,
                parentRelationType: JobRelationType.AggregatedBy,
                cancellationToken: token).ConfigureAwait(false);
        }

        _logger.LogInformation("[開催日再取得] レース単位の子ジョブを登録しました。Date={Date} Count={Count}", payload.RaceDate, targets.Count);
        await _stateStore.WaitForCollectionDependenciesAsync(
            task.JobType, task.DeduplicationKey, task.LeaseToken, token).ConfigureAwait(false);
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
