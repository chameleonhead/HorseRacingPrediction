using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Api.CollectionController;

public static class CollectionResetEndpointExtensions
{
    public static IEndpointRouteBuilder MapCollectionResetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/collection/reset").WithTags("Collection Tasks API");
        group.MapGet("", async (ProcessingStateStore store, CollectionResetCoordinator coordinator, CancellationToken token) =>
            Results.Ok(new
            {
                status = coordinator.GetStatus(),
                preview = await store.GetResetPreviewAsync(token),
                eventStoreTables = await coordinator.GetEventStoreTableCountsAsync(token)
            }));
        group.MapPost("", (CollectionResetRequest request, CollectionResetCoordinator coordinator) =>
        {
            if (!Valid(request, "キューを初期化")) return Results.BadRequest();
            return coordinator.TryStartQueueReset("admin-api", request.Reason) ? Results.Accepted() : Results.Conflict();
        });
        group.MapPost("/full", (CollectionResetRequest request, CollectionResetCoordinator coordinator) =>
        {
            if (!Valid(request, "収集データを完全初期化")) return Results.BadRequest();
            return coordinator.TryStartFullReset("admin-api", request.Reason, request.ReauthenticationPassword ?? string.Empty)
                ? Results.Accepted() : Results.Conflict();
        });
        return endpoints;
    }

    private static bool Valid(CollectionResetRequest request, string confirmation)
        => string.Equals(request.Confirmation, confirmation, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(request.Reason);

    private sealed record CollectionResetRequest(string Confirmation, string Reason, string? ReauthenticationPassword = null);
}
