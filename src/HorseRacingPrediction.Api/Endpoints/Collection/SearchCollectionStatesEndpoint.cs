using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class SearchCollectionStatesEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/states",
            async (string? statuses, CollectionResourceType? resourceType, string? provider,
                string? definitionId, string? search, int? page, int? pageSize,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                IReadOnlyCollection<CollectionStateStatus>? parsed = null;
                if (!string.IsNullOrWhiteSpace(statuses))
                {
                    var values = new List<CollectionStateStatus>();
                    foreach (var value in statuses.Split(',', StringSplitOptions.RemoveEmptyEntries
                                 | StringSplitOptions.TrimEntries))
                    {
                        if (!Enum.TryParse<CollectionStateStatus>(value, true, out var status))
                            return Results.BadRequest(new { message = $"Unknown state status: {value}" });
                        values.Add(status);
                    }
                    parsed = values;
                }
                return Results.Ok(await store.SearchStatesAsync(new(parsed, resourceType, provider,
                    definitionId, search, page ?? 1, pageSize ?? 50), token));
            });
}
