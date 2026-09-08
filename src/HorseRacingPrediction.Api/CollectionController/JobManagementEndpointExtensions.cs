using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Api.CollectionController;

public static class JobManagementEndpointExtensions
{
    /// <summary>
    /// /pause で一時停止した状態をDBのマーカーとして永続化する際のキー。
    /// Program.cs起動時の状態復元でも同じキーを参照する。
    /// </summary>
    public const string MaintenanceMarkerType = "CollectionMaintenance";
    public const string MaintenanceMarkerKey = "Paused";

    public static IEndpointRouteBuilder MapJobManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/jobs").WithTags("Job Management API");
        group.MapGet("/queue-state", async (ProcessingStateStore store, CollectionMaintenanceState maintenance, CancellationToken token) =>
        {
            var state = await store.GetCollectionPipelineStateAsync(token);
            return Results.Ok(new
            {
                isPaused = state.IsPaused || maintenance.IsActive,
                collectorOnly = state.IsPaused || maintenance.IsCollectorOnly,
                state.Reason,
                state.JobId,
                state.StoppedAt
            });
        });
        group.MapGet("", async (string? jobType, AgentJobStatus? status, int? limit, ProcessingStateStore store, CancellationToken token) =>
            Results.Ok(await store.GetJobStatusesAsync(jobType, status, limit ?? 100, token)));
        group.MapGet("/search", async (string? view, string? query, string? targetDate, string? jobType, AgentJobStatus? status, int? page, int? pageSize, ProcessingStateStore store, CancellationToken token) =>
            Results.Ok(await store.SearchJobStatusesAsync(view, query, targetDate, jobType, status, page ?? 1, pageSize ?? 50, token)));
        group.MapGet("/{jobId}", async (string jobId, ProcessingStateStore store, CancellationToken token) =>
        {
            var detail = await store.GetJobDetailAsync(jobId, token);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
        group.MapPost("/{jobId}/rerun", async (string jobId, JobOperationRequest request, ProcessingStateStore store, CancellationToken token) =>
            ToResult(await store.RerunJobAsync(jobId, request.ExpectedUpdatedAt, "Admin UI", request.Reason, DateTimeOffset.UtcNow, token)));
        group.MapPost("/{jobId}/reacquire", async (string jobId, JobOperationRequest request, ProcessingStateStore store, CancellationToken token) =>
        {
            var result = await store.ReacquireCompletedJobAsync(jobId, request.ExpectedUpdatedAt, "Admin UI", request.Reason, DateTimeOffset.UtcNow, token);
            return result.Result switch
            {
                ForceRequeueJobResult.Requeued => Results.Accepted($"/api/admin/jobs/{Uri.EscapeDataString(result.JobId!)}", new { jobId = result.JobId }),
                ForceRequeueJobResult.NotFound => Results.NotFound(),
                _ => Results.Conflict()
            };
        });
        group.MapPost("/{jobId}/hold", async (string jobId, JobOperationRequest request, ProcessingStateStore store, CancellationToken token) =>
            ToResult(await store.SetJobHoldAsync(jobId, true, request.ExpectedUpdatedAt, "Admin UI", token)));
        group.MapPost("/{jobId}/release-hold", async (string jobId, JobOperationRequest request, ProcessingStateStore store, CancellationToken token) =>
            ToResult(await store.SetJobHoldAsync(jobId, false, request.ExpectedUpdatedAt, "Admin UI", token)));
        group.MapPost("/pause", async (CollectionMaintenanceState maintenance, ProcessingStateStore store, CancellationToken token) =>
        {
            await store.PauseCollectionAsync("管理画面またはWorkerから収集停止が要求されました。", null, token);
            maintenance.TryBegin(collectorOnly: true);
            return Results.Accepted();
        });
        group.MapPost("/resume", async (ProcessingStateStore store, CollectionMaintenanceState maintenance, CancellationToken token) =>
        {
            var count = await store.ResumeCollectionAsync(token);
            maintenance.End();
            return Results.Ok(new { status = "Running", requeued = count, deadLettered = 0 });
        });
        return endpoints;
    }

    private static IResult ToResult(ForceRequeueJobResult result) => result switch
    {
        ForceRequeueJobResult.Requeued => Results.Accepted(),
        ForceRequeueJobResult.NotFound => Results.NotFound(),
        _ => Results.Conflict()
    };

    private sealed record JobOperationRequest(DateTimeOffset ExpectedUpdatedAt, string? Reason = null);
}
