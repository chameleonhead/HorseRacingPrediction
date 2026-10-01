using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class ListFailureNotificationsEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/failure-notifications",
            async ([AsParameters] ListFailureNotificationsRequest request,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                var actionableOnly = string.Equals(request.View, "Actionable", StringComparison.OrdinalIgnoreCase);
                var unpublishedOnly = string.Equals(request.View, "Unpublished", StringComparison.OrdinalIgnoreCase);
                if (!actionableOnly && !unpublishedOnly)
                    return Results.BadRequest(new { message = "view must be Actionable or Unpublished." });
                var published = request.Published;
                if (unpublishedOnly && published is true)
                    return Results.BadRequest(new { message = "published=true cannot be combined with view=Unpublished." });

                CollectionFailureResolutionStatus? parsedState = null;
                if (!string.IsNullOrWhiteSpace(request.DeliveryState))
                {
                    if (!Enum.TryParse<CollectionFailureResolutionStatus>(request.DeliveryState, true, out var state))
                        return Results.BadRequest(new { message = $"Unknown delivery state: {request.DeliveryState}" });
                    parsedState = state;
                }
                var notifications = await store.GetFailureNotificationsAsync(JstTime.Now(),
                    Math.Clamp(request.Limit ?? 100, 1, 1000), actionableOnly, unpublishedOnly,
                    published, parsedState, token);
                return Results.Ok(new ListFailureNotificationsResponse(
                    notifications.Select(CollectionContractMapper.ToDto).ToArray()));
            })
            .WithName("ListFailureNotifications")
            .WithTags("Collection Platform")
            .WithDescription("view is required and must be Actionable or Unpublished. published, deliveryState, and limit further filter the selected view.")
            .Produces<ListFailureNotificationsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);
}
