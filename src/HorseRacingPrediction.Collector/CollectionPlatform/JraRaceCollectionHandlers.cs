using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Workflow;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public sealed class JraRaceDiscoveryCollectionHandler(
    HorseRacingPrediction.Collector.Scheduling.ScrapingRegistrationService registration)
    : ICollectionDefinitionHandler
{
    public CollectionDefinitionId DefinitionId => new("race-discovery");
    public ResourceType ResourceType => ResourceType.Race;

    public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
        CancellationToken cancellationToken)
    {
        await registration.RunOneCycleAsync(cancellationToken).ConfigureAwait(false);
        return new(CollectionAttemptResult.Succeeded,
            PageIdentification: $"RaceDiscovery:JRA:{task.Resource.Id}");
    }
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
        var result = await workflows(session).RefreshAsync(raceId, domainRaceId, cancellationToken).ConfigureAwait(false);
        return new(CollectionAttemptResult.Succeeded, RequestedUrl: ToUri(result.SourceUrl),
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
        var result = task.Attributes.TryGetValue("domainRaceId", out var domainRaceId)
            ? await workflow.RefreshAsync(raceId, domainRaceId, cancellationToken).ConfigureAwait(false)
            : await workflow.CollectAsync(raceId, cancellationToken).ConfigureAwait(false);
        if (result.Errors.Count > 0)
            return new(CollectionAttemptResult.ValidationFailure, "DomainWriteRejected",
                string.Join("; ", result.Errors), RequestedUrl: ToUri(result.SourceUrl));
        if (!result.IsOfficiallyConfirmed)
            return new(CollectionAttemptResult.ResourceNotYetAvailable, "ResultNotConfirmed",
                "Race result is not officially confirmed.", RequestedUrl: ToUri(result.SourceUrl),
                RetryAt: DateTimeOffset.UtcNow.AddMinutes(10));
        return new(CollectionAttemptResult.Succeeded, RequestedUrl: ToUri(result.SourceUrl),
                FinalUrl: ToUri(result.SourceUrl), PageIdentification: $"RaceResult:JRA:{task.Resource.Id}")
            ;
    }

    private static Uri? ToUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}
