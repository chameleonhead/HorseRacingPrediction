using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetFailureNotificationGroupEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/failure-notification-groups/{groupKey}",
            async ([AsParameters] GetFailureNotificationGroupRequest request,
                CollectionPlatformStore store, CancellationToken token) =>
            {
                try
                {
                    var result = await store.GetActionableFailureGroupPageAsync(request.GroupKey, JstTime.Now(),
                        request.Search, request.Page ?? 1, request.PageSize ?? 50, token);
                    return result is null ? Results.NotFound()
                        : Results.Ok(new GetFailureNotificationGroupResponse(CollectionContractMapper.ToDto(result)));
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { message = exception.Message });
                }
            }).Produces<GetFailureNotificationGroupResponse>(StatusCodes.Status200OK);
}
