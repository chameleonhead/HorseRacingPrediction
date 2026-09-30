using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class CreateCollectionRecoveryBatchEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v2/admin/collection/recovery-batches",
            async (CollectionRecoveryBatchRequest request, CollectionPlatformStore store,
                CancellationToken token) =>
            {
                if (string.Equals(request.SelectorType, "NotificationIds", StringComparison.Ordinal))
                {
                    if (request.NotificationIds is null || request.GroupKey is not null
                        || request.ExpectedNotificationIds is not null)
                        return Results.BadRequest(new { message = "NotificationIds selector accepts only notificationIds." });
                    var selectedIds = request.NotificationIds.Distinct().ToHashSet();
                    if (selectedIds.Count == 0)
                        return Results.BadRequest(new { message = "At least one notification is required." });
                    if (selectedIds.Count > 10000)
                        return Results.BadRequest(new { message = "At most 10000 notifications can be recovered at once." });
                    var pending = await store.GetActionableFailureNotificationsAsync(JstTime.Now(), 10000, token);
                    var selected = pending.Where(x => selectedIds.Contains(x.NotificationId)).ToList();
                    if (selected.Count != selectedIds.Count)
                        return Results.Conflict(new { message = "Some failures are no longer pending. Refresh and try again." });
                    return await CollectionPlatformEndpointSupport.RecoverFailuresAsync(selected,
                        request.RequestedRevision, request.Lane, request.Priority, store, token);
                }

                if (string.Equals(request.SelectorType, "GroupKey", StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(request.GroupKey) && request.NotificationIds is null
                        && request.ExpectedNotificationIds is not null)
                    {
                        var match = await store.GetActionableFailureGroupAsync(request.GroupKey,
                            JstTime.Now(), token);
                        if (match.MatchingGroupCount == 0) return Results.NotFound();
                        if (match.MatchingGroupCount > 1)
                            return Results.Conflict(new { message = "The failure group key matches multiple groups." });
                        if (match.Notifications.Count > 10000)
                            return Results.BadRequest(new { message = "At most 10000 notifications can be recovered at once." });
                        var currentIds = match.Notifications.Select(x => x.NotificationId).Order().ToArray();
                        var expectedIds = request.ExpectedNotificationIds.Distinct().Order().ToArray();
                        if (!currentIds.SequenceEqual(expectedIds))
                            return Results.Conflict(new { message = "Failure group membership changed. Refresh and try again." });
                        return await CollectionPlatformEndpointSupport.RecoverFailuresAsync(match.Notifications,
                            request.RequestedRevision, request.Lane, request.Priority, store, token);
                    }
                    return Results.BadRequest(new { message = "GroupKey selector requires groupKey and expectedNotificationIds only." });
                }
                return Results.BadRequest(new { message = "selectorType must be NotificationIds or GroupKey." });
            })
            .WithName("CreateCollectionRecoveryBatch")
            .WithTags("Collection Platform")
            .WithDescription("selectorType is required and must be NotificationIds or GroupKey; selector-specific fields from the other variant are rejected.")
            .Produces<CollectionFailureRecoveryResult>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
}
