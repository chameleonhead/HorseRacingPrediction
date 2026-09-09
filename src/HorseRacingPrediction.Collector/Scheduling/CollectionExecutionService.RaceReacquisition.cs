using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed partial class CollectionExecutionService
{
    private async Task ExecuteSingleRaceReacquisitionAsync(LeasedCollectionTask task, DateTimeOffset now, CancellationToken token)
    {
        var published = await ReacquireRaceAsync(AgentJobPayloadSerializer.Deserialize<RaceReacquisitionPayload>(task.Payload), token);
        if (published) await _stateStore.CompleteCollectionTaskAsync(task.JobType, task.DeduplicationKey, task.LeaseToken, token);
        else await _stateStore.RequeueCollectionTaskAsync(task.JobType, task.DeduplicationKey, task.LeaseToken,
            now.AddMinutes(30), "公開待ち", token);
    }

    private async Task<bool> ReacquireRaceAsync(RaceReacquisitionPayload payload, CancellationToken token)
    {
        var course = RaceCourseNames.Parse(payload.Racecourse);
        if (course == RaceCourse.Unknown || payload.RaceNumber is < 1 or > 12 || string.IsNullOrWhiteSpace(payload.RaceId))
            throw new InvalidOperationException("再取得対象の識別情報が不正です。");
        var raceId = new RaceId(payload.RaceDate, course, payload.RaceNumber);
        await using var session = await _sessionFactory.CreateAsync(token);
        var published = false;
        var errors = new List<string>();
        // 定期収集の公開期間制限は自動巡回向けであり、管理者が明示した再取得には
        // 適用しない。過去日もJRA側でページが辿れる限り出馬表（馬主を含む）を試す。
        try
        {
            var card = await _raceCardWorkflowFactory(session).RefreshAsync(raceId, payload.RaceId, token);
            if (card.Error is not null) errors.Add(card.Error);
            else published = true;
        }
        catch (JraNavigationException ex) when (ex.Reason is JraNavigationFailureReason.NotYetPublished or JraNavigationFailureReason.OutOfDisplayedRange) { }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not TimeoutException && !HorseRacingPrediction.Scraping.Jra.Workflow.ApiFailureClassifier.IsFatalServerError(ex))
        { errors.Add("出馬表: " + ex.Message); }
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Jst).Date);
        if (payload.RaceDate <= today)
        {
            try
            {
                var result = await _raceResultWorkflowFactory(session).RefreshAsync(raceId, payload.RaceId, token);
                errors.AddRange(result.Errors);
                if (result.Errors.Count == 0) published = true;
            }
            catch (JraNavigationException ex) when (ex.Reason == JraNavigationFailureReason.NotYetPublished) { }
        }
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
        return published;
    }
}
