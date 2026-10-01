using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class ListFailureNotificationGroupsEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/failure-notification-groups",
            async ([AsParameters] ListFailureNotificationGroupsRequest request,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                var notifications = await store.GetActionableFailureNotificationsAsync(
                    JstTime.Now(), Math.Clamp(request.Limit ?? 5000, 1, 10000), token);
                return Results.Ok(new ListFailureNotificationGroupsResponse(
                    CollectionFailureGrouping.Build(notifications).Select(CollectionContractMapper.ToDto).ToArray()));
            }).Produces<ListFailureNotificationGroupsResponse>(StatusCodes.Status200OK);
}
