using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class ListCollectionTasksEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/tasks",
            async ([AsParameters] ListCollectionTasksRequest request,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                IReadOnlyCollection<CollectionTaskStatus>? parsedStatuses = null;
                if (!string.IsNullOrWhiteSpace(request.Statuses))
                {
                    var values = new List<CollectionTaskStatus>();
                    foreach (var value in request.Statuses.Split(',', StringSplitOptions.RemoveEmptyEntries
                                 | StringSplitOptions.TrimEntries))
                    {
                        if (!Enum.TryParse<CollectionTaskStatus>(value, true, out var parsed))
                            return Results.BadRequest(new { message = $"Unknown task status: {value}" });
                        values.Add(parsed);
                    }
                    if (request.Status is not null) values.Add(request.Status.Value);
                    parsedStatuses = values.Distinct().ToArray();
                }
                else if (request.Status is not null)
                    parsedStatuses = [request.Status.Value];

                if (request.CreatedFrom > request.CreatedTo)
                    return Results.BadRequest(new { message = "createdFrom must not be later than createdTo." });
                var result = await store.SearchTasksAsync(new(parsedStatuses, request.ResourceType, request.Provider,
                    request.DefinitionId, request.Lane, request.Search, request.CreatedFrom, request.CreatedTo,
                    request.ErrorSearch, request.Page ?? 1, request.PageSize ?? request.Limit ?? 50,
                    request.ActionableOnly ?? false, request.LatestOnly ?? false), token);
                return Results.Ok(new ListCollectionTasksResponse(CollectionContractMapper.ToDto(result)));
            }).Produces<ListCollectionTasksResponse>(StatusCodes.Status200OK);
}
