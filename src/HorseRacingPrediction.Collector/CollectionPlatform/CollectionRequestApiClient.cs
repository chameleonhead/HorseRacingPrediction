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
            if (!Enum.TryParse<ResourceType>(item.ResourceType, true, out var resourceType)
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
        using var response = await client.PostAsJsonAsync("api/admin/collection/requests/batch", request,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CollectionRequestBulkResponse>(cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException("Collection request batch response was empty.");
    }

    public async Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, int requestedRevision,
        CollectionReason reason,
        CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
        IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("api/admin/collection/requests", new
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
