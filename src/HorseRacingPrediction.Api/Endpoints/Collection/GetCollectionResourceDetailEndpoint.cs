using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetCollectionResourceDetailEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/resources/{type}/{provider}/{resourceId}/definitions/{definition}",
            async ([AsParameters] GetCollectionResourceDetailRequest request,
                CollectionPlatformStore store, CancellationToken token) =>
                await store.GetResourceDetailPagedAsync(new(request.Type, request.Provider, request.ResourceId),
                    new(request.Definition), request.RequestHistoryPage ?? request.HistoryPage ?? 1,
                    request.TaskHistoryPage ?? request.HistoryPage ?? 1,
                    request.AttemptHistoryPage ?? request.HistoryPage ?? 1,
                    request.HistoryPageSize ?? 25, token) is { } detail
                    ? Results.Ok(new GetCollectionResourceDetailResponse(CollectionContractMapper.ToDto(detail)))
                    : Results.NotFound())
            .Produces<GetCollectionResourceDetailResponse>(StatusCodes.Status200OK);
}
