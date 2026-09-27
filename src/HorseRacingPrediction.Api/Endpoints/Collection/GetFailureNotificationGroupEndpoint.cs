using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetFailureNotificationGroupEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/failure-notification-groups/{groupKey}",
            async (string groupKey, string? search, int? page, int? pageSize,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                try
                {
                    var result = await store.GetActionableFailureGroupPageAsync(groupKey, JstTime.Now(),
                        search, page ?? 1, pageSize ?? 50, token);
                    return result is null ? Results.NotFound() : Results.Ok(result);
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { message = exception.Message });
                }
            });
}
