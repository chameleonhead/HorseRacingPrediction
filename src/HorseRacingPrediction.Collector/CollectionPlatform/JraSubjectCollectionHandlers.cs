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
    IJraSessionFactory sessions, IJraSubjectProfileSink sink, ICollectionRequestSink? requests = null)
    : ICollectionDefinitionHandler
{
    private const int MaximumDiscoveryDepth = 3;
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
        if (page is null)
        {
            try
            {
                page = await session.Navigate.ToSubjectProfileAsync(identity, cancellationToken).ConfigureAwait(false);
            }
            catch (JraCollectionException ex) when (ex.Message.Contains("同定不能", StringComparison.Ordinal))
            {
                return new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", ex.Message);
            }
        }
        SubjectProfilePageParser.Validate(page, identity);
        try
        {
            await sink.SaveAsync(descriptor.SubjectType, task.Resource.Id, page.Profile, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // EventFlow read models are eventually consistent. A subject discovered from a race can
            // reach the profile worker before its Horse/Jockey/Trainer projection becomes visible.
            return new(CollectionAttemptResult.ResourceNotYetAvailable, "SubjectProjectionNotReady",
                ex.Message, RetryAt: DateTimeOffset.UtcNow.AddMinutes(1));
        }
        if (descriptor.ResourceType == ResourceType.Horse && requests is not null)
            await DiscoverHorseReferencesAsync(task, page.Profile, requests, cancellationToken).ConfigureAwait(false);
        return new(CollectionAttemptResult.Succeeded, RequestedUrl: new Uri(page.Url), FinalUrl: new Uri(page.Url),
            PageIdentification: $"{descriptor.SubjectType}Profile:JRA:{task.Resource.Id}");
    }

    private static DateOnly? ParseDate(string? value) => DateOnly.TryParse(value, out var result) ? result : null;

    private static async Task DiscoverHorseReferencesAsync(LeasedCollectionTask task, JraSubjectProfileDto profile,
        ICollectionRequestSink sink, CancellationToken cancellationToken)
    {
        var depth = int.TryParse(task.Attributes.GetValueOrDefault("discoveryDepth"), out var parsed) ? parsed : 0;
        if (depth >= MaximumDiscoveryDepth) return;
        var ancestors = (task.Attributes.GetValueOrDefault("discoveryAncestors") ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(task.Resource.Id)
            .ToHashSet(StringComparer.Ordinal);
        static string? Field(IReadOnlyDictionary<string, string> fields, params string[] names) =>
            names.Select(fields.GetValueOrDefault).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
        var references = new (ResourceType Type, string? Name)[]
        {
            (ResourceType.Trainer, Field(profile.Fields, "調教師", "調教師名")),
            (ResourceType.Horse, Field(profile.Fields, "父", "父馬")),
            (ResourceType.Horse, NormalizeDam(Field(profile.Fields, "母", "母馬"))),
        };
        foreach (var reference in references.Where(x => !string.IsNullOrWhiteSpace(x.Name))
                     .Select(x => (x.Type, Name: x.Name!.Trim())).Distinct())
        {
            var child = JraSubjectCollectionDefinitions.For(reference.Type);
            var childId = HorseRacingPrediction.ApiClient.DeterministicIdGenerator.BuildEntityId(
                child.IdPrefix, reference.Name);
            if (ancestors.Contains(childId)) continue;
            await sink.RequestAsync(new(reference.Type, "JRA", childId), child.Definition,
                CollectionReason.Discovery, CollectionLane.Background, Math.Max(10, task.Priority - 10), null,
                task.EffectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                new Dictionary<string, string>
                {
                    ["name"] = reference.Name,
                    ["discoveryDepth"] = (depth + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["discoveryAncestors"] = string.Join('|', ancestors.Order()),
                }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? NormalizeDam(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var marker = value.IndexOfAny(['(', '（']);
        return (marker < 0 ? value : value[..marker]).Trim();
    }
}
