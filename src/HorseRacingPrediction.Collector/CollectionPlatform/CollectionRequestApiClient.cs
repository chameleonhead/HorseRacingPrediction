using System.Net.Http.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public interface ICollectionRequestSink
{
    Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, int requestedRevision,
        CollectionReason reason,
        CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
        IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken);

    async Task<CollectionRequestBulkResponse> RequestManyAsync(CollectionRequestBulkRequest request,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<CollectionRequestBulkOutcome>();
        foreach (var item in request.Items)
        {
            if (!Enum.TryParse<CollectionResourceType>(item.ResourceType, true, out var resourceType)
                || !Enum.TryParse<CollectionReason>(item.Reason, true, out var reason)
                || !Enum.TryParse<CollectionLane>(item.Lane, true, out var lane)
                || item.EffectiveDate is null)
            {
                outcomes.Add(new(item.ItemKey, "Rejected", ErrorCode: "InvalidEnum",
                    Message: "ResourceType, Reason, Lane, or EffectiveDate is invalid."));
                continue;
            }

            await RequestAsync(new(resourceType, item.Provider, item.ResourceId), new(item.DefinitionId),
                item.RequestedRevision, reason,
                lane, item.Priority,
                Uri.TryCreate(item.ExplicitUrl, UriKind.Absolute, out var explicitUrl) ? explicitUrl : null,
                item.EffectiveDate.Value, item.Attributes ?? new Dictionary<string, string>(), cancellationToken);
            outcomes.Add(new(item.ItemKey, "Accepted"));
        }
        return new(outcomes);
    }
}

public sealed class CollectionRequestApiClient(HttpClient client) : ICollectionRequestSink
{
    public async Task<CollectionRequestBulkResponse> RequestManyAsync(CollectionRequestBulkRequest request,
        CancellationToken cancellationToken)
    {
        var submission = new CollectionTaskBatchRequest("ExplicitItems", ExplicitItems: request);
        using var response = await client.PostAsJsonAsync("api/v2/admin/collection/task-batches", submission,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CollectionTaskBatchSubmissionResponse>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Collection task batch response was empty.");
        if (!string.Equals(result.Mode, "ExplicitItems", StringComparison.Ordinal)
            || result.ExplicitItems is null)
            throw new InvalidOperationException("Collection task batch response did not contain explicit-item outcomes.");
        return result.ExplicitItems;
    }

    public async Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, int requestedRevision,
        CollectionReason reason,
        CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
        IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("api/v2/admin/collection/tasks", new
        {
            ResourceType = resource.Type,
            resource.Provider,
            ResourceId = resource.Id,
            DefinitionId = definition.Value,
            RequestedRevision = requestedRevision,
            Reason = reason,
            Lane = lane,
            Priority = priority,
            ExplicitUrl = explicitUrl?.AbsoluteUri,
            EffectiveDate = effectiveDate,
            Attributes = attributes,
            BatchId = attributes.GetValueOrDefault("batchId"),
        }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
