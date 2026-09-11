using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Workflow;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public sealed class JraRaceDiscoveryCollectionHandler(IJraSessionFactory sessions,
    JraScheduleCollectionWorkflowFactory schedules, ICollectionRequestSink requests)
    : ICollectionDefinitionHandler
{
    public CollectionDefinitionId DefinitionId => new("race-discovery");
    public ResourceType ResourceType => ResourceType.Race;

    public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
        CancellationToken cancellationToken)
    {
        var referenceDate = task.EffectiveDate
            ?? throw new InvalidOperationException("Discovery effective date is required.");
        await using var session = await sessions.CreateAsync(cancellationToken).ConfigureAwait(false);
        var schedule = schedules(session);
        for (var offset = -7; offset <= 7; offset++)
        {
            var date = referenceDate.AddDays(offset);
            var courses = await schedule.CollectAsync(date, cancellationToken).ConfigureAwait(false);
            foreach (var course in courses.Where(x => x != RaceCourse.Unknown))
            {
                var page = offset >= 0
                    ? await session.Navigate.ToRaceListAsync(date, course, cancellationToken).ConfigureAwait(false)
                    : await session.Navigate.ToRaceResultListAsync(date, course, cancellationToken).ConfigureAwait(false);
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
                    if (offset >= 0)
                        await requests.RequestAsync(new(ResourceType.RaceCard, "JRA", id), new("race-card"),
                            CollectionReason.Discovery, CollectionLane.Realtime, 80, ToUri(race.RaceCardUrl), date,
                            attributes, cancellationToken).ConfigureAwait(false);
                    if (offset < 0 || !string.IsNullOrWhiteSpace(race.ResultUrl))
                        await requests.RequestAsync(new(ResourceType.RaceResult, "JRA", id), new("race-result"),
                            CollectionReason.Discovery, offset >= 0 ? CollectionLane.Realtime : CollectionLane.Background,
                            offset >= 0 ? 100 : 10, ToUri(race.ResultUrl), date, attributes, cancellationToken)
                            .ConfigureAwait(false);
                }
            }
        }
        return new(CollectionAttemptResult.Succeeded,
            PageIdentification: $"RaceDiscovery:JRA:{task.Resource.Id}");
    }

    private static Uri? ToUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}

public sealed class JraRaceCardCollectionHandler(IJraSessionFactory sessions,
    JraRaceCardCollectionWorkflowFactory workflows) : ICollectionDefinitionHandler
{
    public CollectionDefinitionId DefinitionId => new("race-card");
    public ResourceType ResourceType => ResourceType.RaceCard;

    public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
        CancellationToken cancellationToken)
    {
        var raceId = ParseRaceId(task);
        var domainRaceId = task.Attributes.GetValueOrDefault("domainRaceId") ?? task.Resource.Id;
        await using var session = await sessions.CreateAsync(cancellationToken).ConfigureAwait(false);
        var workflow = workflows(session);
        RaceCardRaceOutcome? result = null;
        Uri? successfulLocation = null;
        foreach (var location in task.Locations ?? [])
        {
            try
            {
                var page = await session.Navigate.ToUrlAsync(location.Url, cancellationToken).ConfigureAwait(false);
                if (page is not JraRaceCardPage card || card.RaceId != raceId) continue;
                result = await workflow.RefreshPageAsync(card, domainRaceId, cancellationToken).ConfigureAwait(false);
                successfulLocation = location.Url;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A location is only a candidate. A stale URL, parse failure, or transient navigation
                // failure must not prevent trying the remaining candidates or the normal discovery route.
            }
        }
        result ??= await workflow.RefreshAsync(raceId, domainRaceId, cancellationToken).ConfigureAwait(false);
        var requestedUrl = successfulLocation ?? ToUri(result.SourceUrl);
        return new(CollectionAttemptResult.Succeeded, RequestedUrl: requestedUrl,
            FinalUrl: ToUri(result.SourceUrl), PageIdentification: $"RaceCard:JRA:{task.Resource.Id}");
    }

    private static Uri? ToUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    internal static RaceId ParseRaceId(LeasedCollectionTask task)
    {
        if (task.EffectiveDate is null) throw new InvalidOperationException("Race effective date is required.");
        if (!task.Attributes.TryGetValue("course", out var course)
            || !task.Attributes.TryGetValue("number", out var numberText)
            || !int.TryParse(numberText, out var number))
            throw new InvalidOperationException("Race course and number attributes are required.");
        return new(task.EffectiveDate.Value, RaceCourseNames.Parse(course), number);
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
        await using var session = await sessions.CreateAsync(cancellationToken).ConfigureAwait(false);
        var workflow = workflows(session);
        var domainRaceId = task.Attributes.GetValueOrDefault("domainRaceId") ?? task.Resource.Id;
        RaceResultCollectionResult? result = null;
        Uri? successfulLocation = null;
        foreach (var location in task.Locations ?? [])
        {
            try
            {
                var page = await session.Navigate.ToUrlAsync(location.Url, cancellationToken).ConfigureAwait(false);
                if (page is not JraRaceResultPage resultPage || resultPage.RaceId != raceId) continue;
                result = await workflow.RefreshPageAsync(resultPage, domainRaceId, string.Empty, cancellationToken)
                    .ConfigureAwait(false);
                successfulLocation = location.Url;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Continue through candidate locations before invoking the normal discovery navigation.
            }
        }
        result ??= await workflow.RefreshAsync(raceId, domainRaceId, cancellationToken).ConfigureAwait(false);
        if (result.Errors.Count > 0)
            return new(CollectionAttemptResult.ValidationFailure, "DomainWriteRejected",
                string.Join("; ", result.Errors), RequestedUrl: ToUri(result.SourceUrl));
        if (!result.IsOfficiallyConfirmed)
            return new(CollectionAttemptResult.ResourceNotYetAvailable, "ResultNotConfirmed",
                "Race result is not officially confirmed.", RequestedUrl: ToUri(result.SourceUrl),
                RetryAt: DateTimeOffset.UtcNow.AddMinutes(10));
        return new(CollectionAttemptResult.Succeeded, RequestedUrl: successfulLocation ?? ToUri(result.SourceUrl),
                FinalUrl: ToUri(result.SourceUrl), PageIdentification: $"RaceResult:JRA:{task.Resource.Id}")
            ;
    }

    private static Uri? ToUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}
