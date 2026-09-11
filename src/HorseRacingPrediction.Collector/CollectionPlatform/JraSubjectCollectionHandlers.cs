using System.Net.Http.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public sealed record JraSubjectCollectionDefinition(ResourceType ResourceType,
    CollectionDefinitionId Definition, string SubjectType, string IdPrefix);

public static class JraSubjectCollectionDefinitions
{
    public static IReadOnlyList<JraSubjectCollectionDefinition> All { get; } =
    [
        new(ResourceType.Horse, new("horse-profile"), "Horse", "horse"),
        new(ResourceType.Jockey, new("jockey-profile"), "Jockey", "jockey"),
        new(ResourceType.Trainer, new("trainer-profile"), "Trainer", "trainer"),
    ];

    public static JraSubjectCollectionDefinition For(ResourceType type) =>
        All.SingleOrDefault(x => x.ResourceType == type)
        ?? throw new InvalidOperationException($"No JRA subject collection definition exists for {type}.");
}

public interface IJraSubjectProfileSink
{
    Task SaveAsync(string subjectType, string subjectId, JraSubjectProfileDto profile,
        CancellationToken cancellationToken);
}

public sealed class JraSubjectProfileApiClient(HttpClient client) : IJraSubjectProfileSink
{
    public async Task SaveAsync(string subjectType, string subjectId, JraSubjectProfileDto profile,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"api/admin/subjects/{subjectType}/{Uri.EscapeDataString(subjectId)}/profile", profile,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class JraSubjectProfileCollectionHandler(JraSubjectCollectionDefinition descriptor,
    IJraSessionFactory sessions, IJraSubjectProfileSink sink) : ICollectionDefinitionHandler
{
    public CollectionDefinitionId DefinitionId => descriptor.Definition;
    public ResourceType ResourceType => descriptor.ResourceType;

    public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
        CancellationToken cancellationToken)
    {
        var name = task.Attributes.GetValueOrDefault("name")
            ?? throw new InvalidOperationException("Subject name attribute is required.");
        var identity = new JraSubjectIdentity(descriptor.SubjectType, name,
            ParseDate(task.Attributes.GetValueOrDefault("birthDate")),
            task.Attributes.GetValueOrDefault("sourceIdentity"));
        await using var session = await sessions.CreateAsync(cancellationToken).ConfigureAwait(false);
        JraSubjectPage? page = null;
        foreach (var location in task.Locations ?? [])
        {
            try
            {
                var candidate = await session.Navigate.ToUrlAsync(location.Url, cancellationToken).ConfigureAwait(false);
                if (candidate is not JraSubjectPage subjectPage) continue;
                SubjectProfilePageParser.Validate(subjectPage, identity);
                page = subjectPage;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        page ??= await session.Navigate.ToSubjectProfileAsync(identity, cancellationToken).ConfigureAwait(false);
        SubjectProfilePageParser.Validate(page, identity);
        await sink.SaveAsync(descriptor.SubjectType, task.Resource.Id, page.Profile, cancellationToken)
            .ConfigureAwait(false);
        return new(CollectionAttemptResult.Succeeded, RequestedUrl: new Uri(page.Url), FinalUrl: new Uri(page.Url),
            PageIdentification: $"{descriptor.SubjectType}Profile:JRA:{task.Resource.Id}");
    }

    private static DateOnly? ParseDate(string? value) => DateOnly.TryParse(value, out var result) ? result : null;
}
