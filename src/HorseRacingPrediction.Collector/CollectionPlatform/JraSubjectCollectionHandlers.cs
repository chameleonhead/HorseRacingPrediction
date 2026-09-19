using System.Net.Http.Json;
using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public sealed record JraSubjectCollectionDefinition(ResourceType ResourceType,
    CollectionDefinitionId Definition, string SubjectType, string IdPrefix, bool PersistProfile = true,
    int CurrentRevision = 3);

public static class JraSubjectCollectionDefinitions
{
    public static IReadOnlyList<JraSubjectCollectionDefinition> All { get; } =
    [
        new(ResourceType.Horse, new("horse-profile"), "Horse", "horse"),
        new(ResourceType.Jockey, new("jockey-profile"), "Jockey", "jockey"),
        new(ResourceType.Trainer, new("trainer-profile"), "Trainer", "trainer"),
        new(ResourceType.Owner, new("owner-identity"), "Owner", "owner", false, 1),
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

public interface IOwnerIdentityVerifier
{
    Task<bool> ExistsAsync(string ownerId, CancellationToken cancellationToken);
}

public sealed class OwnerIdentityApiClient(HttpClient client) : IOwnerIdentityVerifier
{
    public async Task<bool> ExistsAsync(string ownerId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"api/owners/{Uri.EscapeDataString(ownerId)}?take=1", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }
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
    IJraSessionFactory sessions, IJraSubjectProfileSink sink, ICollectionRequestSink? requests = null,
    TimeProvider? timeProvider = null, IOwnerIdentityVerifier? ownerIdentities = null,
    IDataCollectionWriteService? entityWriter = null)
    : ICollectionDefinitionHandler
{
    private const int MaximumDiscoveryDepth = 3;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public CollectionDefinitionId DefinitionId => descriptor.Definition;
    public ResourceType ResourceType => descriptor.ResourceType;

    public async Task<CollectionAttemptCompletion> CollectAsync(LeasedCollectionTask task,
        CancellationToken cancellationToken)
    {
        var name = task.Attributes.GetValueOrDefault("name");
        if (string.IsNullOrWhiteSpace(name))
            return IdentificationFailure(task, "主体名がないため識別できません。", "MissingName",
                null, null, []);
        var identity = new JraSubjectIdentity(descriptor.SubjectType, name,
            ParseDate(task.Attributes.GetValueOrDefault("birthDate")),
            task.Attributes.GetValueOrDefault("sourceIdentity"));
        if (descriptor.ResourceType == ResourceType.Owner)
        {
            if (ownerIdentities is null || !await ownerIdentities.ExistsAsync(
                    task.Resource.Id, cancellationToken).ConfigureAwait(false))
                return IdentificationFailure(task, "RaceEntryに対応する馬主を確認できません。",
                    "OwnerNotRegistered", null, null, []);
            return new(CollectionAttemptResult.Succeeded,
                PageIdentification: $"OwnerIdentity:JRA:{task.Resource.Id}");
        }
        return await JraSessionExecutionScope.ExecuteWithClosedSessionRetryAsync(sessions,
            (session, token) => CollectWithSessionAsync(task, identity, session, token), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<CollectionAttemptCompletion> CollectWithSessionAsync(LeasedCollectionTask task,
        JraSubjectIdentity identity, JraSession session, CancellationToken cancellationToken)
    {
        JraSubjectPage? page = null;
        var locationOutcomes = new List<ResourceLocationOutcome>();
        foreach (var location in task.Locations ?? [])
        {
            if (IsParameterlessJraAccessUrl(location.Url))
            {
                locationOutcomes.Add(ResourceLocationOutcomeClassifier.Unexpected(
                    location, "NonTerminalJraUrlIgnored"));
                continue;
            }
            try
            {
                var candidate = await session.Navigate.ToUrlAsync(location.Url, cancellationToken).ConfigureAwait(false);
                if (candidate is not JraSubjectPage subjectPage)
                {
                    locationOutcomes.Add(ResourceLocationOutcomeClassifier.Unexpected(location, "SubjectPageTypeMismatch"));
                    continue;
                }
                SubjectProfilePageParser.Validate(subjectPage, identity);
                page = subjectPage;
                locationOutcomes.Add(ResourceLocationOutcomeClassifier.Succeeded(location));
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                locationOutcomes.Add(ResourceLocationOutcomeClassifier.Failed(location, ex));
            }
        }
        if (page is null)
        {
            // A persisted source URL is useful while validating that URL, but must not constrain
            // discovery after the URL itself has failed. Keep the stable name/birth-date identity
            // and let navigation discover the subject's current official URL.
            var discoveryIdentity = identity with { SourceIdentity = null };
            try
            {
                page = await session.Navigate.ToSubjectProfileAsync(discoveryIdentity, cancellationToken).ConfigureAwait(false);
            }
            catch (JraSubjectIdentificationException ex)
            {
                if (descriptor.ResourceType is ResourceType.Jockey or ResourceType.Trainer
                    && ex.Kind == JraSubjectIdentificationFailureKind.NoCandidate)
                    return new(CollectionAttemptResult.NotApplicable, "SubjectNotInProviderDirectory", ex.Message,
                        ToUri(ex.RequestedUrl), ToUri(ex.FinalUrl),
                        PageIdentification: $"SubjectIdentification:{ex.Kind}",
                        LocationOutcomes: locationOutcomes);
                return IdentificationFailure(task, ex, locationOutcomes);
            }
            catch (JraCollectionException ex) when (ex.Message.Contains("同定不能", StringComparison.Ordinal))
            {
                return IdentificationFailure(task, ex.Message, null, null, null, locationOutcomes);
            }
            catch (JraCollectionException ex) when (IsStructuralProfileFailure(ex))
            {
                return new(CollectionAttemptResult.PermanentFailure, "StructuralPageFailure", ex.Message,
                    PageIdentification: $"{descriptor.SubjectType}Profile:UnexpectedPage",
                    LocationOutcomes: locationOutcomes,
                    FailureImpact: CollectionFailureImpact.Isolated);
            }
        }
        try
        {
            SubjectProfilePageParser.Validate(page, identity with { SourceIdentity = null });
        }
        catch (JraSubjectIdentificationException ex)
        {
            return IdentificationFailure(task, ex, locationOutcomes);
        }
        if (descriptor.PersistProfile) try
            {
                await sink.SaveAsync(descriptor.SubjectType, task.Resource.Id, page.Profile, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new(CollectionAttemptResult.PermanentFailure, "SubjectResourceMissing",
                    "The API-authoritative subject resource was not found while persisting its profile. "
                    + ex.Message, LocationOutcomes: locationOutcomes,
                    FailureImpact: CollectionFailureImpact.Isolated);
            }
        if (descriptor.ResourceType == ResourceType.Horse && requests is not null)
        {
            await DiscoverHorseReferencesAsync(task, page.Profile, requests, cancellationToken).ConfigureAwait(false);
            await DiscoverHorseRaceHistoryAsync(task, page, session.Navigate, requests, cancellationToken).ConfigureAwait(false);
        }
        return new(CollectionAttemptResult.Succeeded, RequestedUrl: new Uri(page.Url), FinalUrl: new Uri(page.Url),
            PageIdentification: $"{descriptor.SubjectType}Profile:JRA:{task.Resource.Id}",
            LocationOutcomes: locationOutcomes);
    }

    private static DateOnly? ParseDate(string? value) => DateOnly.TryParse(value, out var result) ? result : null;

    private static bool IsParameterlessJraAccessUrl(Uri url) =>
        string.Equals(url.Host, "www.jra.go.jp", StringComparison.OrdinalIgnoreCase)
        && url.AbsolutePath.StartsWith("/JRADB/access", StringComparison.OrdinalIgnoreCase)
        && url.AbsolutePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrWhiteSpace(url.Query.TrimStart('?'));

    private static CollectionAttemptCompletion IdentificationFailure(LeasedCollectionTask task,
        JraSubjectIdentificationException exception, IReadOnlyList<ResourceLocationOutcome> locationOutcomes) =>
        IdentificationFailure(task, exception.Message, exception.Kind.ToString(), exception.RequestedUrl,
            exception.FinalUrl, locationOutcomes);

    private static CollectionAttemptCompletion IdentificationFailure(LeasedCollectionTask task,
        string message, string? failureKind, string? requestedUrl, string? finalUrl,
        IReadOnlyList<ResourceLocationOutcome> locationOutcomes)
    {
        return new(
            CollectionAttemptResult.ResourceNotFound,
            "SubjectNotIdentified", message,
            ToUri(requestedUrl), ToUri(finalUrl),
            PageIdentification: failureKind is null ? "SubjectIdentification:Legacy" : $"SubjectIdentification:{failureKind}",
            LocationOutcomes: locationOutcomes);
    }

    private static Uri? ToUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;

    private static bool IsStructuralProfileFailure(JraCollectionException exception) =>
        exception.Message.Contains("情報の見出しを確認できません", StringComparison.Ordinal);

    private async Task DiscoverHorseReferencesAsync(LeasedCollectionTask task, JraSubjectProfileDto profile,
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
            (ResourceType.Horse, NormalizePedigreeReference(Field(profile.Fields, "父", "父馬"))),
            (ResourceType.Horse, NormalizePedigreeReference(Field(profile.Fields, "母", "母馬"))),
        };
        foreach (var reference in references.Where(x => !string.IsNullOrWhiteSpace(x.Name))
                     .Select(x => (x.Type, Name: x.Name!.Trim())).Distinct())
        {
            var child = JraSubjectCollectionDefinitions.For(reference.Type);
            var childId = reference.Type switch
            {
                ResourceType.Trainer when entityWriter is not null => await entityWriter.UpsertTrainerAsync(
                    reference.Name, null, null, cancellationToken).ConfigureAwait(false),
                ResourceType.Horse when entityWriter is not null => await entityWriter.UpsertHorseAsync(
                    reference.Name, null, null, null, cancellationToken).ConfigureAwait(false),
                _ => DeterministicIdGenerator.BuildEntityId(child.IdPrefix,
                    DeterministicIdGenerator.NormalizeKey(
                        SubjectProfilePageParser.CanonicalizeDisplayName(reference.Type.ToString(), reference.Name))),
            };
            if (ancestors.Contains(childId)) continue;
            await sink.RequestAsync(new(reference.Type, "JRA", childId), child.Definition, child.CurrentRevision,
                CollectionReason.Discovery, CollectionLane.Background, Math.Max(10, task.Priority - 10), null,
                task.EffectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                new Dictionary<string, string>
                {
                    ["name"] = reference.Name,
                    ["discoveryDepth"] = (depth + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["discoveryAncestors"] = string.Join('|', ancestors.Order()),
                    ["discoveredFromType"] = task.Resource.Type.ToString(),
                    ["discoveredFromProvider"] = task.Resource.Provider,
                    ["discoveredFromId"] = task.Resource.Id,
                }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? NormalizePedigreeReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var marker = value.IndexOfAny(['(', '（']);
        var normalized = (marker < 0 ? value : value[..marker]).Trim();
        return normalized.EndsWith("産駒", StringComparison.Ordinal) ? null : normalized;
    }

    private async Task DiscoverHorseRaceHistoryAsync(LeasedCollectionTask task, JraSubjectPage firstPage,
        HorseRacingPrediction.Scraping.Jra.Navigation.IJraNavigator navigator,
        ICollectionRequestSink sink, CancellationToken cancellationToken)
    {
        var priorityUntil = ParseDate(task.Attributes.GetValueOrDefault("weekendPriorityUntil"));
        var (lane, priority) = task.Lane switch
        {
            CollectionLane.Realtime => (CollectionLane.Normal, (int)CollectionPriority.Low),
            _ => (CollectionLane.Background, (int)CollectionPriority.Background),
        };
        var seenPages = new HashSet<string>(StringComparer.Ordinal);
        JraSubjectPage? page = firstPage;
        var pageIndex = 0;
        while (page is not null)
        {
            if (!seenPages.Add(string.Join('|', page.Races.Select(x => x.Key))))
                throw new JraCollectionException("出走履歴のページ送りが進みません。");
            var itemsByRace = new Dictionary<string, CollectionRequestBulkItem>(StringComparer.Ordinal);
            foreach (var history in page.Races.Where(x => x.ExclusionReason is null && x.Link is not null))
            {
                var url = CollectionHttpUrl.Resolve(history.Link!.Url, page.Url);
                if (!TryResolveRaceResult(url, history, out var resource, out var effectiveDate, out var attributes))
                    continue;
                var requestAttributes = new Dictionary<string, string>(attributes)
                {
                    ["requestedByHorseId"] = task.Resource.Id,
                    ["requestedByHorseName"] = task.Attributes.GetValueOrDefault("name") ?? string.Empty,
                };
                if (priorityUntil is not null)
                    requestAttributes["weekendPriorityUntil"] = priorityUntil.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                itemsByRace.TryAdd(resource.Id, new CollectionRequestBulkItem(
                    resource.Id, resource.Type.ToString(), resource.Provider, resource.Id, "race-detail", 2,
                    CollectionReason.Discovery.ToString(), lane.ToString(), priority, url?.AbsoluteUri,
                    effectiveDate, requestAttributes));
            }
            var chunks = itemsByRace.Values.Chunk(500).ToArray();
            for (var chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                var items = chunks[chunkIndex];
                var batchId = $"horse-history:{task.TaskId:N}:p{pageIndex}:c{chunkIndex}";
                var response = await sink.RequestManyAsync(new(batchId, items), cancellationToken)
                    .ConfigureAwait(false);
                ValidateHistoryBatchResponse(items, response);
            }
            pageIndex++;
            page = await navigator.NextHorseHistoryPageAsync(page, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateHistoryBatchResponse(IReadOnlyList<CollectionRequestBulkItem> items,
        CollectionRequestBulkResponse response)
    {
        var expectedKeys = items.Select(item => item.ItemKey).Order(StringComparer.Ordinal).ToArray();
        var actualKeys = response.Outcomes.Select(outcome => outcome.ItemKey).Order(StringComparer.Ordinal).ToArray();
        if (!expectedKeys.SequenceEqual(actualKeys, StringComparer.Ordinal)
            || response.Outcomes.Any(outcome => outcome.Status is not ("Created" or "Reused")
                || outcome.RequestId is null || outcome.RequestId == Guid.Empty
                || outcome.TaskId is null || outcome.TaskId == Guid.Empty))
            throw new InvalidOperationException("Horse history batch response was incomplete or rejected.");
    }

    private static bool TryResolveRaceResult(Uri? url, HorseHistoryRaceLink history, out ResourceKey resource,
        out DateOnly effectiveDate, out IReadOnlyDictionary<string, string> attributes)
    {
        resource = default!;
        effectiveDate = default;
        attributes = new Dictionary<string, string>();
        if (url is null || !string.Equals(url.Host, "www.jra.go.jp", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(url.AbsolutePath, "/JRADB/accessS.html", StringComparison.Ordinal)) return false;
        var match = Regex.Match(Uri.UnescapeDataString(url.Query),
            @"(?:^|[?&])CNAME=pw01sde10(?<course>\d{2})\d{8}(?<number>\d{2})(?<date>\d{8})/",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !DateOnly.TryParseExact(match.Groups["date"].Value, "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out effectiveDate)
            || !int.TryParse(match.Groups["number"].Value, CultureInfo.InvariantCulture, out var number)
            || number is < 1 or > 12 || !CourseCodes.TryGetValue(match.Groups["course"].Value, out var course)
            || history.Date != effectiveDate || RaceCourseNames.Parse(history.Course) != course) return false;
        resource = new(ResourceType.Race, "JRA", $"{effectiveDate:yyyyMMdd}:{course}:{number}");
        attributes = new Dictionary<string, string>
        {
            ["course"] = RaceCourseNames.GetJraName(course),
            ["number"] = number.ToString(CultureInfo.InvariantCulture),
        };
        return true;
    }

    private static readonly IReadOnlyDictionary<string, RaceCourse> CourseCodes =
        new Dictionary<string, RaceCourse>(StringComparer.Ordinal)
        {
            ["01"] = RaceCourse.Sapporo,
            ["02"] = RaceCourse.Hakodate,
            ["03"] = RaceCourse.Fukushima,
            ["04"] = RaceCourse.Niigata,
            ["05"] = RaceCourse.Tokyo,
            ["06"] = RaceCourse.Nakayama,
            ["07"] = RaceCourse.Chukyo,
            ["08"] = RaceCourse.Kyoto,
            ["09"] = RaceCourse.Hanshin,
            ["10"] = RaceCourse.Kokura,
        };
}
