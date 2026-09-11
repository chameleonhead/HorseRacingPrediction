using System.Net.Http.Json;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

public interface ICollectionRequestSink
{
    Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, CollectionReason reason,
        CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
        IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken);
}

public sealed class CollectionRequestApiClient(HttpClient client) : ICollectionRequestSink
{
    public async Task RequestAsync(ResourceKey resource, CollectionDefinitionId definition, CollectionReason reason,
        CollectionLane lane, int priority, Uri? explicitUrl, DateOnly effectiveDate,
        IReadOnlyDictionary<string, string> attributes, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("api/admin/collection/requests", new
        {
            ResourceType = resource.Type, resource.Provider, ResourceId = resource.Id,
            DefinitionId = definition.Value, RequestedRevision = 1, Reason = reason, Lane = lane,
            Priority = priority, ExplicitUrl = explicitUrl?.AbsoluteUri, EffectiveDate = effectiveDate,
            Attributes = attributes,
        }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
