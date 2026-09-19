using System.Security.Cryptography;
using System.Text;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.PredictionScheduling;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using Microsoft.Extensions.Options;
using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public sealed class RaceDiscoveryCollectionOptions
{
    public int NearPublicationRetryMinutes { get; set; } = 180;
    public int DistantPublicationCheckHourJst { get; set; } = 9;
}

public sealed class RaceDetailCollectionOptions
{
    public int ResultCheckGraceMinutes { get; set; } = 5;
    public int SameDayResultRetryMinutes { get; set; } = 10;
    public int HistoricalResultInitialRetryMinutes { get; set; } = 30;
    public int HistoricalResultMaxRetryMinutes { get; set; } = 360;
}
public sealed class JraRaceDiscoveryCollectionHandler(IJraSessionFactory sessions,
    JraScheduleCollectionWorkflowFactory schedules, ICollectionRequestSink requests,
    IOptions<RaceDiscoveryCollectionOptions>? options = null, TimeProvider? timeProvider = null)
    : ICollectionDefinitionHandler
{
    private readonly RaceDiscoveryCollectionOptions _options = options?.Value ?? new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public CollectionDefinitionId DefinitionId => new("race-discovery");
    public ResourceType ResourceType => ResourceType.Race;

    public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
        CancellationToken cancellationToken)
    {
        var referenceDate = task.EffectiveDate
            ?? throw new InvalidOperationException("Discovery effective date is required.");
        await using var sessionLease = await JraSessionExecutionScope.AcquireAsync(sessions, cancellationToken)
            .ConfigureAwait(false);
        var session = sessionLease.Session;
        var schedule = schedules(session);
        var isSingleDayDiscovery = task.Reason is CollectionReason.Backfill
            or CollectionReason.PeriodRecollection;
        var firstOffset = isSingleDayDiscovery ? 0 : -7;
        var lastOffset = isSingleDayDiscovery ? 0 : 7;
        DateOnly? earliestUnpublishedDate = null;
        string? unpublishedMessage = null;
        for (var offset = firstOffset; offset <= lastOffset; offset++)
        {
            var date = referenceDate.AddDays(offset);
            var courses = await schedule.CollectAsync(date, cancellationToken).ConfigureAwait(false);
            foreach (var course in courses.Where(x => x != RaceCourse.Unknown))
            {
                var resultRoute = date < TodayJst().AddDays(-JraNavigator.DefaultRaceCardLookupPeriodDays);
                IJraPage page;
                try
                {
                    page = resultRoute
                        ? await session.Navigate.ToRaceResultListAsync(date, course, cancellationToken).ConfigureAwait(false)
                        : await session.Navigate.ToRaceListAsync(date, course, cancellationToken).ConfigureAwait(false);
                }
                catch (JraNavigationException ex) when (!resultRoute && date < TodayJst()
                    && ex.Reason == JraNavigationFailureReason.OutOfDisplayedRange)
                {
                    page = await session.Navigate.ToRaceResultListAsync(date, course, cancellationToken)
                        .ConfigureAwait(false);
                    resultRoute = true;
                }
                catch (JraNavigationException ex) when (!resultRoute && IsFutureJst(date)
                    && ex.Reason == JraNavigationFailureReason.NotYetPublished)
                {
                    RecordUnpublished(date, ex.Message);
                    continue;
                }
                if (!resultRoute && page is not JraRaceListPage)
                {
                    if (IsFutureJst(date))
                    {
                        RecordUnpublished(date, $"Unexpected page: {page.Kind}");
                        continue;
                    }
                    throw new JraCollectionException(
                        $"レース一覧とは異なるページを検出しました。Date={date:yyyy-MM-dd}, Course={course}, Kind={page.Kind}");
                }
                if (resultRoute && page is not JraRaceListPage && page is not JraRaceResultPage)
                {
                    return new(
                        CollectionAttemptResult.UnexpectedPage,
                        "RaceResultListPageKindMismatch",
                        $"レース結果一覧とは異なるページを検出しました。Date={date:yyyy-MM-dd}, Course={course}, Kind={page.Kind}",
                        FinalUrl: ToAbsoluteUri(page.Url),
                        PageIdentification: $"Expected=RaceListOrRaceResult; Actual={page.Kind}; Resource={task.Resource.Id}");
                }
                if (page is JraRaceListPage parsedList
                    && (parsedList.Date != date || parsedList.Course != course))
                {
                    throw new JraRaceIdentityMismatchException(
                        JraPageKind.RaceList,
                        parsedList.Url,
                        $"{date:yyyy-MM-dd}:{course}",
                        $"{parsedList.Date:yyyy-MM-dd}:{parsedList.Course}");
                }
                var races = page switch
                {
                    JraRaceListPage list => list.Races,
                    JraRaceResultPage result => [new RaceSummary(result.RaceId, null, null, null, result.Url)],
                    _ => [],
                };
                foreach (var race in races)
                {
                    if (race.Id.Date != date || race.Id.Course != course)
                    {
                        throw new JraRaceIdentityMismatchException(
                            page.Kind,
                            page.Url,
                            $"{date:yyyy-MM-dd}:{course}:{race.Number}",
                            race.Id.ToString());
                    }
                    var id = $"{date:yyyyMMdd}:{course}:{race.Number}";
                    var attributes = new Dictionary<string, string>
                    {
                        ["course"] = RaceCourseNames.GetJraName(course),
                        ["number"] = race.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    };
                    if (task.Attributes.TryGetValue("batchId", out var batchId)) attributes["batchId"] = batchId;
                    Uri? detailUrl;
                    if (!resultRoute)
                    {
                        if (race.StartTime is { } detailStart)
                            attributes["startTime"] = detailStart.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                        var cardUrl = JraRaceDetailUrl.Validate(
                            CollectionHttpUrl.Resolve(race.RaceCardUrl, page.Url), ResourceType.RaceCard, race.Id);
                        detailUrl = cardUrl;
                        if (race.StartTime is { } start)
                        {
                            var oddsAttributes = new Dictionary<string, string>(attributes)
                            { ["startTime"] = start.ToString("HH:mm") };
                            await requests.RequestAsync(new(ResourceType.RaceOdds, "JRA", id), new("race-odds"), 1,
                                CollectionReason.Discovery, CollectionLane.Realtime, 90, null, date,
                                oddsAttributes, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        detailUrl = JraRaceDetailUrl.Validate(
                            CollectionHttpUrl.Resolve(race.ResultUrl, page.Url), ResourceType.RaceResult, race.Id);
                    }
                    await requests.RequestAsync(new(ResourceType.Race, "JRA", id), new("race-detail"), 2,
                        task.Reason is CollectionReason.Backfill or CollectionReason.PeriodRecollection
                            ? task.Reason
                            : CollectionReason.Discovery,
                        resultRoute ? CollectionLane.Background : CollectionLane.Realtime,
                        resultRoute ? 10 : 100, detailUrl, date, attributes, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        if (earliestUnpublishedDate is { } waitingDate)
            return PublicationWaiting(waitingDate, unpublishedMessage ?? "Race list is not published.");
        return new(CollectionAttemptResult.Succeeded,
            PageIdentification: $"RaceDiscovery:JRA:{task.Resource.Id}");

        void RecordUnpublished(DateOnly date, string message)
        {
            if (earliestUnpublishedDate is not null && earliestUnpublishedDate <= date) return;
            earliestUnpublishedDate = date;
            unpublishedMessage = message;
        }
    }

    private bool IsFutureJst(DateOnly date) => date > TodayJst();

    private static Uri? ToAbsoluteUri(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    private DateOnly TodayJst()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), zone).DateTime);
    }

    private CollectionAttemptCompletion PublicationWaiting(DateOnly targetDate, string message)
        => new(CollectionAttemptResult.ResourceNotYetAvailable, "RaceListNotYetAvailable", message,
            RetryAt: NextPublicationCheck(targetDate),
            PageIdentification: $"RaceDiscovery:JRA:Waiting:{targetDate:yyyyMMdd}");

    private DateTimeOffset NextPublicationCheck(DateOnly targetDate)
    {
        var now = _time.GetUtcNow();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        if (targetDate.DayNumber - today.DayNumber <= 1)
            return now.AddMinutes(Math.Clamp(_options.NearPublicationRetryMinutes, 15, 1440));

        var nextDate = today.AddDays(1);
        var hour = Math.Clamp(_options.DistantPublicationCheckHourJst, 0, 23);
        var localCheck = nextDate.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified);
        return new DateTimeOffset(localCheck, zone.GetUtcOffset(localCheck)).ToUniversalTime();
    }

}

public sealed class JraRaceDetailCollectionHandler(IJraSessionFactory sessions,
    JraRaceCardCollectionWorkflowFactory cardWorkflows,
    JraRaceResultCollectionWorkflowFactory resultWorkflows, IPredictionSchedule? predictionSchedule = null,
    ICollectionRequestSink? requests = null, TimeProvider? timeProvider = null,
    IOptions<RaceDetailCollectionOptions>? options = null) : ICollectionDefinitionHandler
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly RaceDetailCollectionOptions _options = options?.Value ?? new();
    public CollectionDefinitionId DefinitionId => new("race-detail");
    public ResourceType ResourceType => ResourceType.Race;

    public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
        CancellationToken cancellationToken)
    {
        var raceId = ParseRaceId(task);
        return await JraSessionExecutionScope.ExecuteWithClosedSessionRetryAsync(sessions,
            (session, token) => CollectWithSessionAsync(task, raceId, session, token), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<CollectionAttemptCompletion> CollectWithSessionAsync(LeasedCollectionTask task,
        RaceId raceId, JraSession session, CancellationToken cancellationToken)
    {
        task.Attributes.TryGetValue("domainRaceId", out var domainRaceId);
        var workflow = cardWorkflows(session);
        var today = TodayJst();
        var cardAlreadyCurrent = string.Equals(task.Attributes.GetValueOrDefault("cardArtifactStatus"),
            RaceArtifactStatus.Current.ToString(), StringComparison.OrdinalIgnoreCase);
        var ownerRepair = task.Attributes.TryGetValue("ownerRepair", out var ownerRepairValue)
            && string.Equals(ownerRepairValue, "true", StringComparison.OrdinalIgnoreCase);
        var requiresCard = !cardAlreadyCurrent
            && raceId.Date >= today.AddDays(-JraNavigator.DefaultRaceCardLookupPeriodDays);
        RaceCardRaceOutcome? result = null;
        Uri? successfulLocation = null;
        var locationOutcomes = new List<ResourceLocationOutcome>();
        var stageOutcomes = new List<CollectionStageOutcome>();
        string? cardOwnerError = null;
        RaceSchedulingEvidence? raceEvidence = null;
        if (ownerRepair && !requiresCard)
        {
            stageOutcomes.Add(new("ResolveCard", RaceArtifactKind.Card,
                CollectionAttemptResult.NotApplicable, "OfficialRaceCardOutsideLookupPeriod",
                "対象Raceは出馬表の探索対象期間外です。"));
            return new(CollectionAttemptResult.NotApplicable, "OfficialRaceCardOutsideLookupPeriod",
                "対象Raceは出馬表の探索対象期間外であり、馬主情報は現行の公式取得元から補正できません。",
                StageOutcomes: stageOutcomes);
        }
        foreach (var location in requiresCard
                     ? (task.Locations ?? []).Where(x => x.Artifact is null or RaceArtifactKind.Card)
                     : [])
        {
            JraRaceCardPage? card;
            try
            {
                var page = await session.Navigate.ToUrlAsync(location.Url, cancellationToken).ConfigureAwait(false);
                card = page as JraRaceCardPage;
                if (card is null || card.RaceId != raceId)
                {
                    locationOutcomes.Add(ResourceLocationOutcomeClassifier.Unexpected(location, "RaceCardIdentityMismatch", RaceArtifactKind.Card));
                    continue;
                }
                successfulLocation = location.Url;
                locationOutcomes.Add(ResourceLocationOutcomeClassifier.Succeeded(location, RaceArtifactKind.Card));
                if (card.StartTime is { } observedStart)
                    raceEvidence = new RaceSchedulingEvidence(ToJstInstant(raceId.Date, observedStart),
                        "JRA-RaceCard", _time.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                locationOutcomes.Add(ResourceLocationOutcomeClassifier.Failed(location, ex, RaceArtifactKind.Card));
                continue;
            }
            try
            {
                result = await workflow.RefreshPageAsync(card, domainRaceId, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stageOutcomes.Add(new("PersistCard", RaceArtifactKind.Card,
                    CollectionAttemptResult.ValidationFailure, "RaceCardWriteFailed", ex.Message,
                    location.Url, ToUri(card.Url)));
                return new(CollectionAttemptResult.ValidationFailure, "RaceCardWriteFailed", ex.Message,
                    RequestedUrl: location.Url, FinalUrl: ToUri(card.Url), LocationOutcomes: locationOutcomes,
                    StageOutcomes: stageOutcomes, RaceEvidence: raceEvidence);
            }
        }
        if (requiresCard && result is null)
        {
            try
            {
                result = await workflow.RefreshAsync(raceId, domainRaceId, cancellationToken).ConfigureAwait(false);
            }
            catch (JraNavigationException ex) when (raceId.Date < today
                && ex.Reason == JraNavigationFailureReason.OutOfDisplayedRange)
            {
                if (ownerRepair)
                {
                    stageOutcomes.Add(new("ResolveCard", RaceArtifactKind.Card,
                        CollectionAttemptResult.NotApplicable, "OfficialRaceCardUnavailable", ex.Message));
                    return new(CollectionAttemptResult.NotApplicable, "OfficialRaceCardUnavailable",
                        "公式出馬表を取得できないため、馬主情報は現行の公式取得元から補正できません。",
                        LocationOutcomes: locationOutcomes, StageOutcomes: stageOutcomes);
                }
                // RaceCardLookupPeriod is a local preference, not a guarantee that JRA still exposes
                // the card selection. A retired card for a past race must not prevent result collection.
                requiresCard = false;
            }
            catch (JraPageKindMismatchException ex)
            {
                stageOutcomes.Add(new("ResolveCard", RaceArtifactKind.Card,
                    CollectionAttemptResult.UnexpectedPage, ex.GetType().Name, ex.Message,
                    FinalUrl: ToUri(ex.Url)));
                return new(
                    CollectionAttemptResult.UnexpectedPage,
                    ex.GetType().Name,
                    ex.Message,
                    FinalUrl: ToUri(ex.Url),
                    PageIdentification:
                        $"Expected={ex.ExpectedKind}; Actual={ex.ActualKind}; Resource={ex.ExpectedResourceId ?? task.Resource.Id}",
                    LocationOutcomes: locationOutcomes, StageOutcomes: stageOutcomes);
            }
            catch (JraPageParseException ex)
            {
                stageOutcomes.Add(new("ResolveCard", RaceArtifactKind.Card,
                    CollectionAttemptResult.UnexpectedPage, ex.GetType().Name, ex.Message,
                    FinalUrl: ToUri(ex.Url)));
                return new(
                    CollectionAttemptResult.UnexpectedPage,
                    ex.GetType().Name,
                    ex.Message,
                    FinalUrl: ToUri(ex.Url),
                    PageIdentification: $"Expected=RaceCard; Actual={ex.PageKind}; Resource={task.Resource.Id}",
                    LocationOutcomes: locationOutcomes, StageOutcomes: stageOutcomes);
            }
            catch (JraCollectionException ex)
            {
                if (ownerRepair)
                {
                    stageOutcomes.Add(new("ResolveCard", RaceArtifactKind.Card,
                        CollectionAttemptResult.NotApplicable, "OfficialRaceCardUnavailable", ex.Message));
                    return new(CollectionAttemptResult.NotApplicable, "OfficialRaceCardUnavailable",
                        "公式出馬表を取得できないため、馬主情報は現行の公式取得元から補正できません。",
                        LocationOutcomes: locationOutcomes, StageOutcomes: stageOutcomes);
                }
                stageOutcomes.Add(new("ResolveCard", RaceArtifactKind.Card,
                    CollectionAttemptResult.ResourceNotYetAvailable, "RaceCardNotYetAvailable", ex.Message));
                return new(CollectionAttemptResult.ResourceNotYetAvailable, "RaceCardNotYetAvailable",
                    ex.Message, RetryAt: HorseRacingPrediction.Contracts.Time.JstTime.Now().AddMinutes(30),
                    LocationOutcomes: locationOutcomes, StageOutcomes: stageOutcomes);
            }
        }
        if (raceEvidence is null && result?.StartTime is { } collectedStart)
            raceEvidence = new RaceSchedulingEvidence(ToJstInstant(raceId.Date, collectedStart),
                "JRA-RaceCard", _time.GetUtcNow());
        if (requiresCard && result is { Error: not null })
        {
            stageOutcomes.Add(new("PersistCard", RaceArtifactKind.Card,
                CollectionAttemptResult.ValidationFailure, "RaceCardWriteRejected", result.Error,
                successfulLocation ?? ToUri(result.SourceUrl), ToUri(result.SourceUrl)));
        }
        else if (requiresCard && result is not null)
        {
            stageOutcomes.Add(new("PersistCard", RaceArtifactKind.Card, CollectionAttemptResult.Succeeded,
                RequestedUrl: successfulLocation ?? ToUri(result.SourceUrl), FinalUrl: ToUri(result.SourceUrl),
                Persisted: true));
            var ownerCount = result.Entries?.Count(x => !string.IsNullOrWhiteSpace(x.OwnerName)) ?? 0;
            var entryCount = result.Entries?.Count ?? 0;
            if (entryCount > 0)
            {
                cardOwnerError = ownerCount == entryCount ? null : $"OwnerCount={ownerCount}/{entryCount}";
                stageOutcomes.Add(cardOwnerError is null
                    ? new("ValidateCardOwners", RaceArtifactKind.Card, CollectionAttemptResult.Succeeded,
                        ErrorMessage: $"OwnerCount={ownerCount}/{entryCount}", Persisted: true)
                    : new("ValidateCardOwners", RaceArtifactKind.Card, CollectionAttemptResult.ValidationFailure,
                        "RaceCardOwnerIncomplete", cardOwnerError));
            }
        }
        string? subjectBatchError = null;
        if (requiresCard && result?.Error is null && requests is not null && result?.Entries is not null)
            try
            {
                await RequestReferencedSubjectsAsync(task, result.Entries, result.RaceId!, requests,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ReferencedSubjectBatchException ex)
            {
                subjectBatchError = ex.Message;
            }
        if (requiresCard && result?.Error is null && predictionSchedule is not null)
            await predictionSchedule.EnqueueAsync([result!.RaceId!], HorseRacingPrediction.Contracts.Time.JstTime.Now(), cancellationToken).ConfigureAwait(false);
        var firstResultCheck = FirstResultCheck(raceId.Date, task.Attributes, result?.StartTime);
        if (raceId.Date >= today && firstResultCheck is null)
        {
            var retryAt = _time.GetUtcNow().AddMinutes(30);
            stageOutcomes.Add(new("AwaitOfficialStart", RaceArtifactKind.Result,
                CollectionAttemptResult.ResourceNotYetAvailable, "OfficialStartTimeUnknown",
                "Official start time is not available yet."));
            return new(CollectionAttemptResult.ResourceNotYetAvailable, "OfficialStartTimeUnknown",
                "Official start time is not available yet.", RetryAt: retryAt,
                LocationOutcomes: locationOutcomes, StageOutcomes: stageOutcomes, RaceEvidence: raceEvidence);
        }
        if (firstResultCheck is { } dueAt && _time.GetUtcNow() < dueAt)
        {
            stageOutcomes.Add(new("AwaitOfficialStart", RaceArtifactKind.Result,
                CollectionAttemptResult.ResourceNotYetAvailable, "RaceNotStarted",
                "Race result is not available before the first result check."));
            return new(CollectionAttemptResult.ResourceNotYetAvailable, "RaceNotStarted",
                "Race result is not available before the first result check.", RequestedUrl: successfulLocation ?? ToUri(result?.SourceUrl),
                RetryAt: dueAt, LocationOutcomes: locationOutcomes, StageOutcomes: stageOutcomes,
                RaceEvidence: raceEvidence);
        }

        var resultWorkflow = resultWorkflows(session);
        RaceResultCollectionResult? raceResult = null;
        if (!requiresCard)
        {
            foreach (var location in (task.Locations ?? []).Where(x => x.Artifact is null or RaceArtifactKind.Result))
            {
                JraRaceResultPage? resultPage;
                try
                {
                    var page = await session.Navigate.ToUrlAsync(location.Url, cancellationToken).ConfigureAwait(false);
                    resultPage = page as JraRaceResultPage;
                    if (resultPage is null || resultPage.RaceId != raceId)
                    {
                        locationOutcomes.Add(ResourceLocationOutcomeClassifier.Unexpected(location, "RaceResultIdentityMismatch", RaceArtifactKind.Result));
                        continue;
                    }
                    successfulLocation = location.Url;
                    locationOutcomes.Add(ResourceLocationOutcomeClassifier.Succeeded(location, RaceArtifactKind.Result));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    locationOutcomes.Add(ResourceLocationOutcomeClassifier.Failed(location, ex, RaceArtifactKind.Result));
                    continue;
                }
                try
                {
                    raceResult = await resultWorkflow.RefreshPageAsync(resultPage, domainRaceId, string.Empty,
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    stageOutcomes.Add(new("PersistResult", RaceArtifactKind.Result,
                        CollectionAttemptResult.ValidationFailure, "RaceResultWriteFailed", ex.Message,
                        location.Url, ToUri(resultPage.Url)));
                    return new(CollectionAttemptResult.ValidationFailure, "RaceResultWriteFailed", ex.Message,
                        RequestedUrl: location.Url, FinalUrl: ToUri(resultPage.Url),
                        LocationOutcomes: locationOutcomes, StageOutcomes: stageOutcomes,
                        RaceEvidence: raceEvidence);
                }
            }
        }
        try
        {
            raceResult ??= await resultWorkflow.RefreshAsync(raceId, domainRaceId, cancellationToken).ConfigureAwait(false);
        }
        catch (JraNavigationException ex)
        {
            stageOutcomes.Add(new("ResolveResult", RaceArtifactKind.Result,
                CollectionAttemptResult.ResourceNotYetAvailable, "RaceResultNotYetAvailable", ex.Message));
            return new(CollectionAttemptResult.ResourceNotYetAvailable, "RaceResultNotYetAvailable", ex.Message,
                RequestedUrl: successfulLocation ?? ToUri(result?.SourceUrl), RetryAt: NextResultRetry(raceId.Date),
                LocationOutcomes: locationOutcomes, StageOutcomes: stageOutcomes, RaceEvidence: raceEvidence);
        }
        if (raceResult.Errors.Count > 0)
        {
            stageOutcomes.Add(new("PersistResult", RaceArtifactKind.Result,
                CollectionAttemptResult.ValidationFailure, "DomainWriteRejected",
                string.Join("; ", raceResult.Errors), FinalUrl: ToUri(raceResult.SourceUrl)));
            return new(CollectionAttemptResult.ValidationFailure, "DomainWriteRejected",
                string.Join("; ", raceResult.Errors), RequestedUrl: ToUri(raceResult.SourceUrl),
                LocationOutcomes: locationOutcomes, StageOutcomes: stageOutcomes, RaceEvidence: raceEvidence);
        }
        if (raceResult.IsOfficiallyCancelled)
        {
            stageOutcomes.Add(new("PersistResult", RaceArtifactKind.Result,
                CollectionAttemptResult.NotApplicable, FinalUrl: ToUri(raceResult.SourceUrl), Persisted: true));
            return new(CollectionAttemptResult.Succeeded,
                RequestedUrl: successfulLocation ?? ToUri(result?.SourceUrl) ?? ToUri(raceResult.SourceUrl),
                FinalUrl: ToUri(raceResult.SourceUrl),
                PageIdentification: $"RaceDetailOfficialCancellation:JRA:{task.Resource.Id}",
                LocationOutcomes: locationOutcomes, StageOutcomes: stageOutcomes, RaceEvidence: raceEvidence);
        }
        if (!raceResult.IsOfficiallyConfirmed)
        {
            stageOutcomes.Add(new("ConfirmResult", RaceArtifactKind.Result,
                CollectionAttemptResult.ResourceNotYetAvailable, "ResultNotConfirmed",
                "Race result is not officially confirmed.", FinalUrl: ToUri(raceResult.SourceUrl)));
            return new(CollectionAttemptResult.ResourceNotYetAvailable, "ResultNotConfirmed",
                "Race result is not officially confirmed.", RequestedUrl: ToUri(raceResult.SourceUrl),
                RetryAt: NextResultRetry(raceId.Date), LocationOutcomes: locationOutcomes,
                StageOutcomes: stageOutcomes, RaceEvidence: raceEvidence);
        }
        stageOutcomes.Add(new("PersistResult", RaceArtifactKind.Result, CollectionAttemptResult.Succeeded,
            FinalUrl: ToUri(raceResult.SourceUrl), Persisted: true));
        if (result?.Error is not null)
            return new(CollectionAttemptResult.ValidationFailure, "RaceCardWriteRejected", result.Error,
                RequestedUrl: successfulLocation ?? ToUri(result.SourceUrl), FinalUrl: ToUri(raceResult.SourceUrl),
                PageIdentification: $"RaceDetail:JRA:{task.Resource.Id}", LocationOutcomes: locationOutcomes,
                FailureImpact: CollectionFailureImpact.Isolated, StageOutcomes: stageOutcomes,
                RaceEvidence: raceEvidence);
        if (subjectBatchError is not null)
            return new(CollectionAttemptResult.ValidationFailure, "ReferencedSubjectBatchRejected",
                subjectBatchError, RequestedUrl: successfulLocation ?? ToUri(result?.SourceUrl),
                FinalUrl: ToUri(raceResult.SourceUrl),
                PageIdentification: $"RaceDetail:JRA:{task.Resource.Id}",
                LocationOutcomes: locationOutcomes, FailureImpact: CollectionFailureImpact.Isolated,
                StageOutcomes: stageOutcomes, RaceEvidence: raceEvidence);
        if (cardOwnerError is not null)
            return new(CollectionAttemptResult.ValidationFailure, "RaceCardOwnerIncomplete", cardOwnerError,
                RequestedUrl: successfulLocation ?? ToUri(result?.SourceUrl), FinalUrl: ToUri(raceResult.SourceUrl),
                PageIdentification: $"RaceDetail:JRA:{task.Resource.Id}", LocationOutcomes: locationOutcomes,
                FailureImpact: CollectionFailureImpact.Isolated, StageOutcomes: stageOutcomes,
                RaceEvidence: raceEvidence);
        return new(CollectionAttemptResult.Succeeded, RequestedUrl: successfulLocation ?? ToUri(result?.SourceUrl) ?? ToUri(raceResult.SourceUrl),
            FinalUrl: ToUri(raceResult.SourceUrl), PageIdentification: $"RaceDetail:JRA:{task.Resource.Id}",
            LocationOutcomes: locationOutcomes, StageOutcomes: stageOutcomes, RaceEvidence: raceEvidence);
    }

    private static Uri? ToUri(string? value) => CollectionHttpUrl.TryCreate(value, out var uri) ? uri : null;

    private DateOnly TodayJst()
    {
        var jst = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), jst).DateTime);
    }

    private DateTimeOffset? FirstResultCheck(DateOnly date, IReadOnlyDictionary<string, string> attributes,
        TimeOnly? observedStartTime)
    {
        if (attributes.TryGetValue("officialStartAt", out var instant)
            && DateTimeOffset.TryParse(instant, out var officialStart))
            return officialStart.AddMinutes(Math.Max(0, _options.ResultCheckGraceMinutes));
        var time = observedStartTime;
        if (time is null && attributes.TryGetValue("startTime", out var value)
            && TimeOnly.TryParse(value, out var parsed)) time = parsed;
        return time is null ? null : ToJstInstant(date, time.Value)
            .AddMinutes(Math.Max(0, _options.ResultCheckGraceMinutes));
    }

    private static DateTimeOffset ToJstInstant(DateOnly date, TimeOnly time)
    {
        var jst = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, jst.GetUtcOffset(local)).ToUniversalTime();
    }

    private DateTimeOffset NextResultRetry(DateOnly raceDate)
    {
        var daysElapsed = Math.Max(0, TodayJst().DayNumber - raceDate.DayNumber);
        if (daysElapsed == 0)
            return _time.GetUtcNow().AddMinutes(Math.Max(1, _options.SameDayResultRetryMinutes));
        var exponent = Math.Clamp(daysElapsed - 1, 0, 10);
        var minutes = Math.Min(Math.Max(1, _options.HistoricalResultMaxRetryMinutes),
            Math.Max(1, _options.HistoricalResultInitialRetryMinutes) * Math.Pow(2, exponent));
        return _time.GetUtcNow().AddMinutes(minutes);
    }

    internal static RaceId ParseRaceId(LeasedCollectionTask task)
    {
        if (task.EffectiveDate is null) throw new InvalidOperationException("Race effective date is required.");
        if (task.Attributes.TryGetValue("course", out var course)
            && task.Attributes.TryGetValue("number", out var numberText)
            && int.TryParse(numberText, out var number))
            return new(task.EffectiveDate.Value, RaceCourseNames.Parse(course), number);

        var parts = task.Resource.Id.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length == 3
            && DateOnly.TryParseExact(parts[0], "yyyyMMdd", out var resourceDate)
            && resourceDate == task.EffectiveDate.Value
            && int.TryParse(parts[2], out number))
        {
            var parsedCourse = Enum.TryParse<RaceCourse>(parts[1], true, out var namedCourse)
                ? namedCourse : RaceCourseNames.Parse(parts[1]);
            if (parsedCourse != RaceCourse.Unknown)
                return new(resourceDate, parsedCourse, number);
        }
        throw new InvalidOperationException("Race course and number attributes are required.");
    }

    private async Task RequestReferencedSubjectsAsync(LeasedCollectionTask task,
        IReadOnlyList<RaceEntry> entries, string requestedByRaceId,
        ICollectionRequestSink sink, CancellationToken cancellationToken)
    {
        var jst = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), jst).DateTime);
        var effectiveDate = task.EffectiveDate ?? today;
        var weekendRelated = effectiveDate >= today && effectiveDate <= today.AddDays(7);
        var lane = weekendRelated ? CollectionLane.Realtime : CollectionLane.Normal;
        var priority = weekendRelated ? (int)CollectionPriority.High : (int)CollectionPriority.Low;
        var subjects = entries.SelectMany(entry => new (ResourceType Type, string? Name, string? SourceIdentity)[]
            {
                (ResourceType.Horse, entry.HorseName, entry.HorseSourceIdentity),
                (ResourceType.Jockey, entry.JockeyName, null),
                (ResourceType.Trainer, entry.TrainerName, null),
                (ResourceType.Owner, entry.OwnerName, null),
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => (x.Type, Name: SubjectProfilePageParser.CanonicalizeDisplayName(
                x.Type.ToString(), x.Name!), x.SourceIdentity))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Distinct();
        var items = subjects.Select(subject =>
        {
            var descriptor = JraSubjectCollectionDefinitions.For(subject.Type);
            var id = subject.Type switch
            {
                ResourceType.Horse => DeterministicIdGenerator.BuildHorseId(subject.Name, subject.SourceIdentity),
                ResourceType.Owner => OwnerIdentityContract.CreateId(subject.Name),
                _ => DeterministicIdGenerator.BuildEntityId(descriptor.IdPrefix,
                    DeterministicIdGenerator.NormalizeKey(subject.Name)),
            };
            var attributes = new Dictionary<string, string>
            {
                ["name"] = subject.Name,
                ["requestedByRaceId"] = requestedByRaceId,
                ["weekendPriorityUntil"] = effectiveDate.ToString("yyyy-MM-dd"),
                ["discoveredFromType"] = task.Resource.Type.ToString(),
                ["discoveredFromProvider"] = task.Resource.Provider,
                ["discoveredFromId"] = task.Resource.Id,
            };
            if (JraSourceIdentity.TryNormalizeHorse(subject.SourceIdentity, out _))
            {
                var sourceUrl = JraSourceIdentity.NormalizeHorseUrl(subject.SourceIdentity)!.ToString();
                attributes["sourceIdentity"] = sourceUrl;
                attributes["sourceUrl"] = sourceUrl;
            }
            return new CollectionRequestBulkItem(
                $"{subject.Type}:{id}", subject.Type.ToString(), "JRA", id, descriptor.Definition.Value,
                descriptor.CurrentRevision,
                CollectionReason.Discovery.ToString(), lane.ToString(), priority,
                JraSourceIdentity.NormalizeHorseUrl(subject.SourceIdentity)?.AbsoluteUri,
                effectiveDate, attributes);
        }).GroupBy(item => item.ItemKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (items.Length == 0) return;
        // The payload fingerprint preserves response-loss idempotency while allowing a corrected
        // canonical-ID payload to supersede an older request made by the same parent task.
        var batchId = BuildReferencedSubjectBatchId(task.TaskId, items);
        var response = await sink.RequestManyAsync(new(batchId, items), cancellationToken).ConfigureAwait(false);
        ValidateReferencedSubjectBatchResponse(items, response);
    }

    internal static string BuildReferencedSubjectBatchId(Guid taskId,
        IReadOnlyList<CollectionRequestBulkItem> items)
    {
        var canonicalPayload = string.Join('\n', items.OrderBy(item => item.ItemKey, StringComparer.Ordinal)
            .Select(item => string.Join('\u001f',
                item.ItemKey, item.ResourceType, item.Provider, item.ResourceId, item.DefinitionId,
                item.RequestedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Reason, item.Lane,
                item.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.ExplicitUrl ?? string.Empty, item.EffectiveDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                string.Join('\u001e', (item.Attributes ?? new Dictionary<string, string>())
                    .OrderBy(attribute => attribute.Key, StringComparer.Ordinal)
                    .Select(attribute => $"{attribute.Key}={attribute.Value}")))));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)))
            .ToLowerInvariant()[..24];
        return $"race-subjects:{taskId:N}:{fingerprint}";
    }

    internal static void ValidateReferencedSubjectBatchResponse(
        IReadOnlyList<CollectionRequestBulkItem> items, CollectionRequestBulkResponse response)
    {
        var expectedKeys = items.Select(item => item.ItemKey).Order(StringComparer.Ordinal).ToArray();
        var actualKeys = response.Outcomes.Select(outcome => outcome.ItemKey).Order(StringComparer.Ordinal).ToArray();
        var validStatuses = new HashSet<string>(["Created", "Reused", "Accepted"], StringComparer.Ordinal);
        if (!expectedKeys.SequenceEqual(actualKeys, StringComparer.Ordinal)
            || response.Outcomes.Any(outcome => !validStatuses.Contains(outcome.Status)))
        {
            var returnedKeys = response.Outcomes.Select(outcome => outcome.ItemKey).ToHashSet(StringComparer.Ordinal);
            var allFailures = items.Where(item => !returnedKeys.Contains(item.ItemKey))
                .Select(item => $"{item.ItemKey}:Missing")
                .Concat(response.Outcomes.Where(outcome => !validStatuses.Contains(outcome.Status))
                    .Select(outcome => $"{outcome.ItemKey}:{outcome.ErrorCode ?? outcome.Status}"))
                .ToArray();
            var failures = allFailures
                .Take(20)
                .ToArray();
            var omitted = allFailures.Length - failures.Length;
            var detail = string.Join(", ", failures);
            if (omitted > 0) detail += $", +{omitted} more";
            throw new ReferencedSubjectBatchException(
                $"Referenced subject batch response was incomplete or rejected. Items=[{detail}]");
        }
    }

    internal sealed class ReferencedSubjectBatchException(string message) : InvalidOperationException(message);
}
