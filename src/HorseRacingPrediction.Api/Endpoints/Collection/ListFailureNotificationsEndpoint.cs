using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class ListFailureNotificationsEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/failure-notifications",
            async (string? view, bool? published, string? deliveryState, int? limit,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                var actionableOnly = string.Equals(view, "Actionable", StringComparison.OrdinalIgnoreCase);
                var unpublishedOnly = string.Equals(view, "Unpublished", StringComparison.OrdinalIgnoreCase);
                if (!actionableOnly && !unpublishedOnly)
                    return Results.BadRequest(new { message = "view must be Actionable or Unpublished." });
                if (unpublishedOnly && published is true)
                    return Results.BadRequest(new { message = "published=true cannot be combined with view=Unpublished." });

                CollectionFailureResolutionStatus? parsedState = null;
                if (!string.IsNullOrWhiteSpace(deliveryState))
                {
                    if (!Enum.TryParse<CollectionFailureResolutionStatus>(deliveryState, true, out var state))
                        return Results.BadRequest(new { message = $"Unknown delivery state: {deliveryState}" });
                    parsedState = state;
                }
                return Results.Ok(await store.GetFailureNotificationsAsync(JstTime.Now(),
                    Math.Clamp(limit ?? 100, 1, 1000), actionableOnly, unpublishedOnly,
                    published, parsedState, token));
            })
            .WithName("ListFailureNotifications")
            .WithTags("Collection Platform")
            .WithDescription("view is required and must be Actionable or Unpublished. published, deliveryState, and limit further filter the selected view.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);
}
