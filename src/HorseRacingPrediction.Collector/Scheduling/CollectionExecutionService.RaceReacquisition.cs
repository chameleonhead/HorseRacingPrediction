using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;

namespace HorseRacingPrediction.Collector.Scheduling;

public sealed partial class CollectionExecutionService
{
    private async Task ExecuteRaceReacquisitionJobsAsync(DateTimeOffset now, CancellationToken token)
    {
        var jobs = await _stateStore.AcquireReadyJobsAsync(AgentJobType.RaceReacquisition, now, TimeSpan.Zero,
            _options.CollectionBatchSize, TimeSpan.FromMinutes(Math.Max(1, _options.CollectionLeaseMinutes)), token);
        foreach (var job in jobs)
        {
            using var timeout = CreateJobTimeoutCts(token);
            try
            {
                var published = await ReacquireRaceAsync(AgentJobPayloadSerializer.Deserialize<RaceReacquisitionPayload>(job.Payload), timeout.Token);
                if (published) await _stateStore.CompleteJobAsync(AgentJobType.RaceReacquisition, job.DeduplicationKey, token);
                else await _stateStore.RequeueJobAsync(AgentJobType.RaceReacquisition, job.DeduplicationKey, now, "公開待ち", token, now.AddMinutes(30));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                await _stateStore.RequeueJobAsync(AgentJobType.RaceReacquisition, job.DeduplicationKey, now, "処理が中断されました。", CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                await _stateStore.FailJobAsync(AgentJobType.RaceReacquisition, job.DeduplicationKey, ex.Message, CancellationToken.None);
                await PausePipelineIfFatalErrorAsync(ex, job.DeduplicationKey);
            }
        }
    }

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
        if (session.Navigate.IsWithinRaceCardLookupPeriod(payload.RaceDate))
        {
            try
            {
                var card = await _raceCardWorkflowFactory(session).RefreshAsync(raceId, payload.RaceId, token);
                if (card.Error is not null) errors.Add(card.Error);
                else published = true;
            }
            catch (JraNavigationException ex) when (ex.Reason is JraNavigationFailureReason.NotYetPublished or JraNavigationFailureReason.OutOfDisplayedRange) { }
            catch (Exception ex) when (ex is not OperationCanceledException && !HorseRacingPrediction.Scraping.Jra.Workflow.ApiFailureClassifier.IsFatalServerError(ex))
            { errors.Add("出馬表: " + ex.Message); }
        }
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
