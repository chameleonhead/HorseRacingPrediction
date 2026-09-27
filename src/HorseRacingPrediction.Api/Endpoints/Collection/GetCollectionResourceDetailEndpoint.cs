using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetCollectionResourceDetailEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/resources/{type}/{provider}/{resourceId}/definitions/{definition}",
            async (CollectionResourceType type, string provider, string resourceId, string definition,
                int? historyPage, int? requestHistoryPage, int? taskHistoryPage, int? attemptHistoryPage,
                int? historyPageSize, CollectionPlatformStore store, CancellationToken token) =>
                await store.GetResourceDetailPagedAsync(new(type, provider, resourceId), new(definition),
                    requestHistoryPage ?? historyPage ?? 1, taskHistoryPage ?? historyPage ?? 1,
                    attemptHistoryPage ?? historyPage ?? 1, historyPageSize ?? 25, token) is { } detail
                    ? Results.Ok(detail) : Results.NotFound());
}
