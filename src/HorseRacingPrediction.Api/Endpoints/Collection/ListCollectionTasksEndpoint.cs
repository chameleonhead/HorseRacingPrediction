using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class ListCollectionTasksEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/tasks",
            async (CollectionTaskStatus? status, int? limit, string? statuses,
                CollectionResourceType? resourceType, string? provider, string? definitionId,
                CollectionLane? lane, string? search, string? errorSearch,
                DateTimeOffset? createdFrom, DateTimeOffset? createdTo,
                bool? actionableOnly, bool? latestOnly, int? page, int? pageSize,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                IReadOnlyCollection<CollectionTaskStatus>? parsedStatuses = null;
                if (!string.IsNullOrWhiteSpace(statuses))
                {
                    var values = new List<CollectionTaskStatus>();
                    foreach (var value in statuses.Split(',', StringSplitOptions.RemoveEmptyEntries
                                 | StringSplitOptions.TrimEntries))
                    {
                        if (!Enum.TryParse<CollectionTaskStatus>(value, true, out var parsed))
                            return Results.BadRequest(new { message = $"Unknown task status: {value}" });
                        values.Add(parsed);
                    }
                    if (status is not null) values.Add(status.Value);
                    parsedStatuses = values.Distinct().ToArray();
                }
                else if (status is not null)
                    parsedStatuses = [status.Value];

                if (createdFrom > createdTo)
                    return Results.BadRequest(new { message = "createdFrom must not be later than createdTo." });
                return Results.Ok(await store.SearchTasksAsync(new(parsedStatuses, resourceType, provider,
                    definitionId, lane, search, createdFrom, createdTo, errorSearch, page ?? 1,
                    pageSize ?? limit ?? 50, actionableOnly ?? false, latestOnly ?? false), token));
            });
}
