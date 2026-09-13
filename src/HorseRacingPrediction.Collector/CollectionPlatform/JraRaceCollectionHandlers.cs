using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.PredictionScheduling;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public sealed class RaceDiscoveryCollectionOptions
{
    public int NearPublicationRetryMinutes { get; set; } = 180;
    public int DistantPublicationCheckHourJst { get; set; } = 9;
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
        var firstOffset = task.Reason == CollectionReason.Backfill ? 0 : -7;
        var lastOffset = task.Reason == CollectionReason.Backfill ? 0 : 7;
        DateOnly? earliestUnpublishedDate = null;
        string? unpublishedMessage = null;
        for (var offset = firstOffset; offset <= lastOffset; offset++)
        {
            var date = referenceDate.AddDays(offset);
            var courses = await schedule.CollectAsync(date, cancellationToken).ConfigureAwait(false);
            foreach (var course in courses.Where(x => x != RaceCourse.Unknown))
            {
                var historical = date < TodayJst().AddDays(-JraNavigator.DefaultRaceCardLookupPeriodDays);
                IJraPage page;
                try
                {
                    page = historical
                        ? await session.Navigate.ToRaceResultListAsync(date, course, cancellationToken).ConfigureAwait(false)
                        : await session.Navigate.ToRaceListAsync(date, course, cancellationToken).ConfigureAwait(false);
                }
                catch (JraNavigationException ex) when (!historical && IsFutureJst(date)
                    && ex.Reason == JraNavigationFailureReason.NotYetPublished)
                {
                    RecordUnpublished(date, ex.Message);
                    continue;
                }
                if (!historical && page is not JraRaceListPage)
                {
                    if (IsFutureJst(date))
                    {
                        RecordUnpublished(date, $"Unexpected page: {page.Kind}");
                        continue;
                    }
                    throw new JraCollectionException(
                        $"レース一覧とは異なるページを検出しました。Date={date:yyyy-MM-dd}, Course={course}, Kind={page.Kind}");
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
                    if (!historical)
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
                            await requests.RequestAsync(new(ResourceType.RaceOdds, "JRA", id), new("race-odds"),
                                CollectionReason.Discovery, CollectionLane.Realtime, 90, null, date,
                                oddsAttributes, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        detailUrl = JraRaceDetailUrl.Validate(
                            CollectionHttpUrl.Resolve(race.ResultUrl, page.Url), ResourceType.RaceResult, race.Id);
                    }
                    await requests.RequestAsync(new(ResourceType.Race, "JRA", id), new("race-detail"),
                        task.Reason == CollectionReason.Backfill ? CollectionReason.Backfill : CollectionReason.Discovery,
                        historical ? CollectionLane.Background : CollectionLane.Realtime,
                        historical ? 10 : 100, detailUrl, date, attributes, cancellationToken).ConfigureAwait(false);
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
    ICollectionRequestSink? requests = null, TimeProvider? timeProvider = null) : ICollectionDefinitionHandler
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public CollectionDefinitionId DefinitionId => new("race-detail");
    public ResourceType ResourceType => ResourceType.Race;

    public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
        CancellationToken cancellationToken)
    {
        var raceId = ParseRaceId(task);
        task.Attributes.TryGetValue("domainRaceId", out var domainRaceId);
        await using var sessionLease = await JraSessionExecutionScope.AcquireAsync(sessions, cancellationToken)
            .ConfigureAwait(false);
        var session = sessionLease.Session;
        var workflow = cardWorkflows(session);
        var today = TodayJst();
        var requiresCard = raceId.Date >= today.AddDays(-JraNavigator.DefaultRaceCardLookupPeriodDays);
        RaceCardRaceOutcome? result = null;
        Uri? successfulLocation = null;
        var locationOutcomes = new List<ResourceLocationOutcome>();
        foreach (var location in requiresCard ? task.Locations ?? [] : [])
        {
            try
            {
                var page = await session.Navigate.ToUrlAsync(location.Url, cancellationToken).ConfigureAwait(false);
                if (page is not JraRaceCardPage card || card.RaceId != raceId)
                {
                    locationOutcomes.Add(ResourceLocationOutcomeClassifier.Unexpected(location, "RaceCardIdentityMismatch"));
                    continue;
                }
                result = await workflow.RefreshPageAsync(card, domainRaceId, cancellationToken).ConfigureAwait(false);
                successfulLocation = location.Url;
                locationOutcomes.Add(ResourceLocationOutcomeClassifier.Succeeded(location));
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                locationOutcomes.Add(ResourceLocationOutcomeClassifier.Failed(location, ex));
            }
        }
        if (requiresCard && result is null)
        {
            try
            {
                result = await workflow.RefreshAsync(raceId, domainRaceId, cancellationToken).ConfigureAwait(false);
            }
            catch (JraPageKindMismatchException ex)
            {
                return new(
                    CollectionAttemptResult.UnexpectedPage,
                    ex.GetType().Name,
                    ex.Message,
                    FinalUrl: ToUri(ex.Url),
                    PageIdentification:
                        $"Expected={ex.ExpectedKind}; Actual={ex.ActualKind}; Resource={ex.ExpectedResourceId ?? task.Resource.Id}",
                    LocationOutcomes: locationOutcomes);
            }
            catch (JraPageParseException ex)
            {
                return new(
                    CollectionAttemptResult.UnexpectedPage,
                    ex.GetType().Name,
                    ex.Message,
                    FinalUrl: ToUri(ex.Url),
                    PageIdentification: $"Expected=RaceCard; Actual={ex.PageKind}; Resource={task.Resource.Id}",
                    LocationOutcomes: locationOutcomes);
            }
            catch (JraCollectionException ex)
            {
                return new(CollectionAttemptResult.ResourceNotYetAvailable, "RaceCardNotYetAvailable",
                    ex.Message, RetryAt: HorseRacingPrediction.Contracts.Time.JstTime.Now().AddMinutes(30),
                    LocationOutcomes: locationOutcomes);
            }
        }
        if (requiresCard && requests is not null && result?.Entries is not null)
            await RequestReferencedSubjectsAsync(task, result.Entries, result.RaceId!, requests, cancellationToken)
                .ConfigureAwait(false);
        if (requiresCard && predictionSchedule is not null)
            await predictionSchedule.EnqueueAsync([result!.RaceId!], HorseRacingPrediction.Contracts.Time.JstTime.Now(), cancellationToken).ConfigureAwait(false);
        var firstResultCheck = FirstResultCheck(raceId.Date, task.Attributes);
        if (_time.GetUtcNow() < firstResultCheck)
            return new(CollectionAttemptResult.ResourceNotYetAvailable, "RaceNotStarted",
                "Race result is not available before the first result check.", RequestedUrl: successfulLocation ?? ToUri(result?.SourceUrl),
                RetryAt: firstResultCheck, LocationOutcomes: locationOutcomes);

        var resultWorkflow = resultWorkflows(session);
        RaceResultCollectionResult? raceResult = null;
        if (!requiresCard)
        {
            foreach (var location in task.Locations ?? [])
            {
                try
                {
                    var page = await session.Navigate.ToUrlAsync(location.Url, cancellationToken).ConfigureAwait(false);
                    if (page is not JraRaceResultPage resultPage || resultPage.RaceId != raceId)
                    {
                        locationOutcomes.Add(ResourceLocationOutcomeClassifier.Unexpected(location, "RaceResultIdentityMismatch"));
                        continue;
                    }
                    raceResult = await resultWorkflow.RefreshPageAsync(resultPage, domainRaceId, string.Empty, cancellationToken)
                        .ConfigureAwait(false);
                    successfulLocation = location.Url;
                    locationOutcomes.Add(ResourceLocationOutcomeClassifier.Succeeded(location));
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    locationOutcomes.Add(ResourceLocationOutcomeClassifier.Failed(location, ex));
                }
            }
        }
        try
        {
            raceResult ??= await resultWorkflow.RefreshAsync(raceId, domainRaceId, cancellationToken).ConfigureAwait(false);
        }
        catch (JraNavigationException ex)
        {
            return new(CollectionAttemptResult.ResourceNotYetAvailable, "RaceResultNotYetAvailable", ex.Message,
                RequestedUrl: successfulLocation ?? ToUri(result?.SourceUrl), RetryAt: _time.GetUtcNow().AddMinutes(10),
                LocationOutcomes: locationOutcomes);
        }
        if (raceResult.Errors.Count > 0)
            return new(CollectionAttemptResult.ValidationFailure, "DomainWriteRejected",
                string.Join("; ", raceResult.Errors), RequestedUrl: ToUri(raceResult.SourceUrl),
                LocationOutcomes: locationOutcomes);
        if (!raceResult.IsOfficiallyConfirmed)
            return new(CollectionAttemptResult.ResourceNotYetAvailable, "ResultNotConfirmed",
                "Race result is not officially confirmed.", RequestedUrl: ToUri(raceResult.SourceUrl),
                RetryAt: _time.GetUtcNow().AddMinutes(10), LocationOutcomes: locationOutcomes);
        return new(CollectionAttemptResult.Succeeded, RequestedUrl: successfulLocation ?? ToUri(result?.SourceUrl) ?? ToUri(raceResult.SourceUrl),
            FinalUrl: ToUri(raceResult.SourceUrl), PageIdentification: $"RaceDetail:JRA:{task.Resource.Id}",
            LocationOutcomes: locationOutcomes);
    }

    private static Uri? ToUri(string? value) => CollectionHttpUrl.TryCreate(value, out var uri) ? uri : null;

    private DateOnly TodayJst()
    {
        var jst = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), jst).DateTime);
    }

    private DateTimeOffset FirstResultCheck(DateOnly date, IReadOnlyDictionary<string, string> attributes)
    {
        var jst = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Tokyo Standard Time" : "Asia/Tokyo");
        var time = attributes.TryGetValue("startTime", out var value) && TimeOnly.TryParse(value, out var parsed)
            ? parsed.AddMinutes(5) : new TimeOnly(9, 35);
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, jst.GetUtcOffset(local)).ToUniversalTime();
    }

    internal static RaceId ParseRaceId(LeasedCollectionTask task)
    {
        if (task.EffectiveDate is null) throw new InvalidOperationException("Race effective date is required.");
        if (!task.Attributes.TryGetValue("course", out var course)
            || !task.Attributes.TryGetValue("number", out var numberText)
            || !int.TryParse(numberText, out var number))
            throw new InvalidOperationException("Race course and number attributes are required.");
        return new(task.EffectiveDate.Value, RaceCourseNames.Parse(course), number);
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
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => (x.Type, Name: x.Name!.Trim(), x.SourceIdentity))
            .Distinct();
        foreach (var subject in subjects)
        {
            var descriptor = JraSubjectCollectionDefinitions.For(subject.Type);
            var id = subject.Type == ResourceType.Horse
                ? DeterministicIdGenerator.BuildHorseId(subject.Name, subject.SourceIdentity)
                : DeterministicIdGenerator.BuildEntityId(descriptor.IdPrefix, subject.Name);
            var attributes = new Dictionary<string, string>
            {
                ["name"] = subject.Name,
                ["requestedByRaceId"] = requestedByRaceId,
                ["weekendPriorityUntil"] = effectiveDate.ToString("yyyy-MM-dd"),
            };
            if (JraSourceIdentity.TryNormalizeHorse(subject.SourceIdentity, out _))
                attributes["sourceIdentity"] = JraSourceIdentity.NormalizeHorseUrl(subject.SourceIdentity)!.ToString();
            await sink.RequestAsync(new(subject.Type, "JRA", id), descriptor.Definition,
                CollectionReason.Discovery, lane, priority,
                JraSourceIdentity.NormalizeHorseUrl(subject.SourceIdentity),
                effectiveDate,
                attributes, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
