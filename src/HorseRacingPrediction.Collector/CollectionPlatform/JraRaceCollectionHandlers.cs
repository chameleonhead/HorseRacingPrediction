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
                var historical = task.Reason == CollectionReason.Backfill || offset < 0;
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
                var races = page switch
                {
                    JraRaceListPage list => list.Races,
                    JraRaceResultPage result => [new RaceSummary(result.RaceId, null, null, null, result.Url)],
                    _ => [],
                };
                foreach (var race in races)
                {
                    var id = $"{date:yyyyMMdd}:{course}:{race.Number}";
                    var attributes = new Dictionary<string, string>
                    {
                        ["course"] = RaceCourseNames.GetJraName(course),
                        ["number"] = race.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    };
                    if (task.Attributes.TryGetValue("batchId", out var batchId)) attributes["batchId"] = batchId;
                    if (!historical)
                    {
                        await requests.RequestAsync(new(ResourceType.RaceCard, "JRA", id), new("race-card"),
                            CollectionReason.Discovery, CollectionLane.Realtime, 80, ToUri(race.RaceCardUrl), date,
                            attributes, cancellationToken).ConfigureAwait(false);
                        if (race.StartTime is { } start)
                        {
                            var oddsAttributes = new Dictionary<string, string>(attributes)
                            { ["startTime"] = start.ToString("HH:mm") };
                            await requests.RequestAsync(new(ResourceType.RaceOdds, "JRA", id), new("race-odds"),
                                CollectionReason.Discovery, CollectionLane.Realtime, 90, null, date,
                                oddsAttributes, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    if (historical || !string.IsNullOrWhiteSpace(race.ResultUrl))
                        await requests.RequestAsync(new(ResourceType.RaceResult, "JRA", id), new("race-result"),
                            task.Reason == CollectionReason.Backfill ? CollectionReason.Backfill : CollectionReason.Discovery,
                            historical ? CollectionLane.Background : CollectionLane.Realtime,
                            historical ? 10 : 100, ToUri(race.ResultUrl), date, attributes, cancellationToken)
                            .ConfigureAwait(false);
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

    private static Uri? ToUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}

public sealed class JraRaceCardCollectionHandler(IJraSessionFactory sessions,
    JraRaceCardCollectionWorkflowFactory workflows, IPredictionSchedule? predictionSchedule = null,
    ICollectionRequestSink? requests = null) : ICollectionDefinitionHandler
{
    public CollectionDefinitionId DefinitionId => new("race-card");
    public ResourceType ResourceType => ResourceType.RaceCard;

    public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
        CancellationToken cancellationToken)
    {
        var raceId = ParseRaceId(task);
        task.Attributes.TryGetValue("domainRaceId", out var domainRaceId);
        await using var sessionLease = await JraSessionExecutionScope.AcquireAsync(sessions, cancellationToken)
            .ConfigureAwait(false);
        var session = sessionLease.Session;
        var workflow = workflows(session);
        RaceCardRaceOutcome? result = null;
        Uri? successfulLocation = null;
        var locationOutcomes = new List<ResourceLocationOutcome>();
        foreach (var location in task.Locations ?? [])
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
        if (result is null)
        {
            try
            {
                result = await workflow.RefreshAsync(raceId, domainRaceId, cancellationToken).ConfigureAwait(false);
            }
            catch (JraCollectionException ex) when (IsCurrentOrFuture(task.EffectiveDate))
            {
                return new(CollectionAttemptResult.ResourceNotYetAvailable, "RaceCardNotYetAvailable",
                    ex.Message, RetryAt: DateTimeOffset.UtcNow.AddMinutes(30),
                    LocationOutcomes: locationOutcomes);
            }
        }
        if (requests is not null && result.Entries is not null)
            await RequestReferencedSubjectsAsync(result.Entries, result.RaceId!, requests, cancellationToken).ConfigureAwait(false);
        if (predictionSchedule is not null)
            await predictionSchedule.EnqueueAsync([result.RaceId!], DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        var requestedUrl = successfulLocation ?? ToUri(result.SourceUrl);
        return new(CollectionAttemptResult.Succeeded, RequestedUrl: requestedUrl,
            FinalUrl: ToUri(result.SourceUrl), PageIdentification: $"RaceCard:JRA:{task.Resource.Id}",
            LocationOutcomes: locationOutcomes);
    }

    private static Uri? ToUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    private static bool IsCurrentOrFuture(DateOnly? date)
    {
        if (date is null) return false;
        var jst = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, jst).DateTime);
        return date >= today;
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

    private static async Task RequestReferencedSubjectsAsync(IReadOnlyList<RaceEntry> entries, string requestedByRaceId,
        ICollectionRequestSink sink, CancellationToken cancellationToken)
    {
        var subjects = entries.SelectMany(entry => new (ResourceType Type, string? Name)[]
            {
                (ResourceType.Horse, entry.HorseName),
                (ResourceType.Jockey, entry.JockeyName),
                (ResourceType.Trainer, entry.TrainerName),
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => (x.Type, Name: x.Name!.Trim()))
            .Distinct();
        foreach (var subject in subjects)
        {
            var descriptor = JraSubjectCollectionDefinitions.For(subject.Type);
            var id = DeterministicIdGenerator.BuildEntityId(descriptor.IdPrefix, subject.Name);
            await sink.RequestAsync(new(subject.Type, "JRA", id), descriptor.Definition,
                CollectionReason.Discovery, CollectionLane.Normal, (int)CollectionPriority.Low, null,
                DateOnly.FromDateTime(DateTime.UtcNow),
                new Dictionary<string, string>
                {
                    ["name"] = subject.Name,
                    ["requestedByRaceId"] = requestedByRaceId,
                }, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

public sealed class JraRaceResultCollectionHandler(IJraSessionFactory sessions,
    JraRaceResultCollectionWorkflowFactory workflows) : ICollectionDefinitionHandler
{
    public CollectionDefinitionId DefinitionId => new("race-result");
    public ResourceType ResourceType => ResourceType.RaceResult;

    public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
        CancellationToken cancellationToken)
    {
        var raceId = JraRaceCardCollectionHandler.ParseRaceId(task);
        await using var sessionLease = await JraSessionExecutionScope.AcquireAsync(sessions, cancellationToken)
            .ConfigureAwait(false);
        var session = sessionLease.Session;
        var workflow = workflows(session);
        task.Attributes.TryGetValue("domainRaceId", out var domainRaceId);
        RaceResultCollectionResult? result = null;
        Uri? successfulLocation = null;
        var locationOutcomes = new List<ResourceLocationOutcome>();
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
                result = await workflow.RefreshPageAsync(resultPage, domainRaceId, string.Empty, cancellationToken)
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
        result ??= await workflow.RefreshAsync(raceId, domainRaceId, cancellationToken).ConfigureAwait(false);
        if (result.Errors.Count > 0)
            return new(CollectionAttemptResult.ValidationFailure, "DomainWriteRejected",
                string.Join("; ", result.Errors), RequestedUrl: ToUri(result.SourceUrl),
                LocationOutcomes: locationOutcomes);
        if (!result.IsOfficiallyConfirmed)
            return new(CollectionAttemptResult.ResourceNotYetAvailable, "ResultNotConfirmed",
                "Race result is not officially confirmed.", RequestedUrl: ToUri(result.SourceUrl),
                RetryAt: DateTimeOffset.UtcNow.AddMinutes(10), LocationOutcomes: locationOutcomes);
        return new(CollectionAttemptResult.Succeeded, RequestedUrl: successfulLocation ?? ToUri(result.SourceUrl),
                FinalUrl: ToUri(result.SourceUrl), PageIdentification: $"RaceResult:JRA:{task.Resource.Id}",
                LocationOutcomes: locationOutcomes)
            ;
    }

    private static Uri? ToUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}
