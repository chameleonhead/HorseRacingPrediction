using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class ListFailureNotificationGroupsEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/failure-notification-groups",
            async (int? limit, CollectionPlatformStore store, CancellationToken token) =>
            {
                var notifications = await store.GetActionableFailureNotificationsAsync(
                    JstTime.Now(), Math.Clamp(limit ?? 5000, 1, 10000), token);
                return Results.Ok(CollectionFailureGrouping.Build(notifications));
            });
}
