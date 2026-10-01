using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class SearchCollectionStatesEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/states",
            async ([AsParameters] SearchCollectionStatesRequest request,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                IReadOnlyCollection<CollectionStateStatus>? parsed = null;
                if (!string.IsNullOrWhiteSpace(request.Statuses))
                {
                    var values = new List<CollectionStateStatus>();
                    foreach (var value in request.Statuses.Split(',', StringSplitOptions.RemoveEmptyEntries
                                 | StringSplitOptions.TrimEntries))
                    {
                        if (!Enum.TryParse<CollectionStateStatus>(value, true, out var status))
                            return Results.BadRequest(new { message = $"Unknown state status: {value}" });
                        values.Add(status);
                    }
                    parsed = values;
                }
                var result = await store.SearchStatesAsync(new(parsed, request.ResourceType, request.Provider,
                    request.DefinitionId, request.Search, request.Page ?? 1, request.PageSize ?? 50), token);
                return Results.Ok(new SearchCollectionStatesResponse(CollectionContractMapper.ToDto(result)));
            }).Produces<SearchCollectionStatesResponse>(StatusCodes.Status200OK);
}
