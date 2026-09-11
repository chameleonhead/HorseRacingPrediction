using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.CollectionController;

public static class CollectionPlatformEndpointExtensions
{
    public static IEndpointRouteBuilder MapCollectionPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/collection").WithTags("Collection Platform");
        admin.MapGet("/tasks", async (CollectionTaskStatus? status, int? limit, CollectionPlatformStore store,
            CancellationToken token) => Results.Ok(await store.GetTasksAsync(status, limit ?? 200, token)));
        admin.MapGet("/progress", async (CollectionPlatformStore store, CancellationToken token) =>
            Results.Ok(await store.GetProgressAsync(token)));
        admin.MapGet("/pipeline", async (CollectionPlatformStore store, CancellationToken token) =>
            Results.Ok(await store.GetPipelineStateAsync(token)));
        admin.MapPost("/pipeline/pause", async (PauseCollectionPipelineRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            await store.SetPausedAsync(true, request.Reason, DateTimeOffset.UtcNow, token);
            return Results.NoContent();
        });
        admin.MapPost("/pipeline/resume", async (CollectionPlatformStore store, CancellationToken token) =>
        {
            await store.SetPausedAsync(false, null, DateTimeOffset.UtcNow, token);
            return Results.NoContent();
        });
        admin.MapPost("/tasks/{taskId:guid}/cancel", async (Guid taskId, CollectionPlatformStore store,
            CancellationToken token) => await store.CancelTaskAsync(taskId, DateTimeOffset.UtcNow, token)
                ? Results.NoContent() : Results.Conflict());
        admin.MapGet("/failure-notifications", async (int? limit, CollectionPlatformStore store,
            CancellationToken token) => Results.Ok(await store.GetPendingFailureNotificationsAsync(
                DateTimeOffset.UtcNow, Math.Clamp(limit ?? 100, 1, 1000), token)));
        admin.MapPost("/failure-notifications/{notificationId:guid}/published", async (Guid notificationId,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            await store.MarkFailureNotificationPublishedAsync(notificationId, DateTimeOffset.UtcNow, token);
            return Results.NoContent();
        });
        admin.MapGet("/states/{type}/{provider}/{resourceId}/{definition}", async (
            ResourceType type, string provider, string resourceId, string definition,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            var state = await store.GetStateAsync(new(type, provider, resourceId), new(definition), token);
            return state is null ? Results.NotFound() : Results.Ok(state);
        });
        admin.MapPost("/requests", async (CreateCollectionRequest request, CollectionPlatformStore store,
            CancellationToken token) =>
        {
            if (!Uri.TryCreate(request.ExplicitUrl, UriKind.Absolute, out var explicitUrl) && request.ExplicitUrl is not null)
                return Results.BadRequest(new { message = "ExplicitUrl must be an absolute URL." });
            var receipt = await store.RequestAsync(new(request.ResourceType, request.Provider, request.ResourceId),
                new(request.DefinitionId), request.RequestedRevision, request.Reason, DateTimeOffset.UtcNow,
                request.Lane, request.Priority, explicitUrl, request.BatchId, request.EffectiveDate,
                request.Attributes, token);
            return Results.Accepted($"/api/admin/collection/tasks/{receipt.TaskId}", receipt);
        });
        admin.MapPost("/requests/bulk", async (BulkCollectionRequest request, CollectionPlatformStore store,
            CancellationToken token) =>
        {
            if (request.Resources.Count is < 1 or > 10_000)
                return Results.BadRequest(new { message = "Resources must contain between 1 and 10000 items." });
            var receipts = new List<CollectionRequestReceipt>(request.Resources.Count);
            foreach (var resource in request.Resources.Distinct())
                receipts.Add(await store.RequestAsync(new(resource.Type, resource.Provider, resource.Id),
                    new(request.DefinitionId), request.RequestedRevision, request.Reason,
                    DateTimeOffset.UtcNow, request.Lane, request.Priority, batchId: request.BatchId,
                    cancellationToken: token));
            return Results.Accepted(value: new { request.BatchId, Requests = receipts });
        });

        var worker = endpoints.MapGroup("/api/internal/collection").WithTags("Collection Worker");
        worker.MapPost("/tasks/{taskId:guid}/acquire", async (Guid taskId, AcquireCollectionTaskRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            var lease = await store.AcquireAsync(taskId, request.DispatchGeneration, DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(Math.Clamp(request.LeaseSeconds, 30, 3600)), token);
            return lease is null ? Results.Conflict() : Results.Ok(lease);
        });
        worker.MapPost("/tasks/{taskId:guid}/complete", async (Guid taskId, CompleteCollectionAttemptRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            Uri.TryCreate(request.RequestedUrl, UriKind.Absolute, out var requestedUrl);
            Uri.TryCreate(request.FinalUrl, UriKind.Absolute, out var finalUrl);
            var accepted = await store.CompleteAttemptAsync(taskId, request.LeaseToken, DateTimeOffset.UtcNow,
                new(request.Result, request.ErrorCode, request.ErrorMessage, requestedUrl, finalUrl,
                    request.HttpStatusCode, request.PageIdentification, request.RetryAt, request.NextCollectionAt), token);
            return accepted ? Results.NoContent() : Results.Conflict();
        });
        worker.MapPost("/tasks/{taskId:guid}/heartbeat", async (Guid taskId,
            HeartbeatCollectionTaskRequest request, CollectionPlatformStore store, CancellationToken token) =>
            await store.HeartbeatAsync(taskId, request.LeaseToken, DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(Math.Clamp(request.LeaseSeconds, 30, 3600)), token)
                ? Results.NoContent() : Results.Conflict());
        return endpoints;
    }
}

public sealed record CreateCollectionRequest(ResourceType ResourceType, string Provider, string ResourceId,
    string DefinitionId, int RequestedRevision, CollectionReason Reason,
    CollectionLane Lane = CollectionLane.Normal, int Priority = (int)CollectionPriority.Normal,
    string? ExplicitUrl = null, string? BatchId = null, DateOnly? EffectiveDate = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

public sealed record AcquireCollectionTaskRequest(long DispatchGeneration, int LeaseSeconds = 900);
public sealed record HeartbeatCollectionTaskRequest(string LeaseToken, int LeaseSeconds = 900);
public sealed record PauseCollectionPipelineRequest(string? Reason);

public sealed record BulkCollectionResource(ResourceType Type, string Provider, string Id);
public sealed record BulkCollectionRequest(string DefinitionId, int RequestedRevision, CollectionReason Reason,
    IReadOnlyList<BulkCollectionResource> Resources, string? BatchId = null,
    CollectionLane Lane = CollectionLane.Background, int Priority = (int)CollectionPriority.Background);

public sealed record CompleteCollectionAttemptRequest(string LeaseToken, CollectionAttemptResult Result,
    string? ErrorCode = null, string? ErrorMessage = null, string? RequestedUrl = null,
    string? FinalUrl = null, int? HttpStatusCode = null, string? PageIdentification = null,
    DateTimeOffset? RetryAt = null, DateTimeOffset? NextCollectionAt = null);
