using HorseRacingPrediction.Collector.Scheduling;

namespace HorseRacingPrediction.Collector.Scheduling;

public static class AgentDashboardEndpointExtensions
{
    public static IEndpointRouteBuilder MapAgentDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var collectionApi = endpoints.MapGroup("/api/collection")
            .WithTags("Collection Tasks API");

        collectionApi.MapGet(
            "/tasks",
            async (
                string? jobType,
                AgentJobStatus? status,
                int? limit,
                IProcessingStateStore stateStore,
                CancellationToken cancellationToken) =>
            {
                var items = await stateStore
                    .GetJobStatusesAsync(jobType, status, limit ?? 100, cancellationToken)
                    .ConfigureAwait(false);
                return Results.Ok(items);
            })
            .WithName("GetCollectionTasks")
            .WithSummary("Get collection tasks");

        collectionApi.MapGet(
            "/tasks/{jobId}",
            async (string jobId, IProcessingStateStore stateStore, CancellationToken cancellationToken) =>
            {
                var detail = await stateStore.GetJobDetailAsync(jobId, cancellationToken).ConfigureAwait(false);
                return detail is null ? Results.NotFound() : Results.Ok(detail);
            })
            .WithName("GetCollectionTask")
            .WithSummary("Get collection task details");

        collectionApi.MapGet(
            "/result-days",
            async (DateOnly from, DateOnly to, IProcessingStateStore stateStore, CancellationToken cancellationToken) =>
            {
                if (from > to)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["from"] = ["from は to 以下である必要があります。"]
                    });
                }

                var items = await stateStore.GetResultDayCollectionStatusesAsync(from, to, cancellationToken).ConfigureAwait(false);
                return Results.Ok(items);
            })
            .WithName("GetCollectionResultDays")
            .WithSummary("Get result-day collection statuses");

        collectionApi.MapPost(
            "/tasks/{jobId}/requeue",
            async (
                string jobId,
                RequeueCollectionTaskRequest request,
                IProcessingStateStore stateStore,
                CollectionExecutionTrigger executionTrigger,
                CancellationToken cancellationToken) =>
            {
                var result = await stateStore.ForceRequeueJobAsync(
                    jobId, request.ExpectedUpdatedAt, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
                if (result == ForceRequeueJobResult.Requeued) executionTrigger.Signal();
                return result switch
                {
                    ForceRequeueJobResult.Requeued => Results.Ok(),
                    ForceRequeueJobResult.NotFound => Results.NotFound(),
                    _ => Results.Conflict(new { message = "ジョブが更新されています。最新状態を確認してから再実行してください。" })
                };
            })
            .WithName("RequeueCollectionTaskById")
            .WithSummary("Requeue a collection task with optimistic concurrency control");

        collectionApi.MapPost(
            "/tasks/{jobId}/cancel",
            async (
                string jobId,
                CancelCollectionTaskRequest request,
                IProcessingStateStore stateStore,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Reason))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["取消理由は必須です。"] });

                var result = await stateStore.CancelJobAsync(
                    jobId, request.ExpectedUpdatedAt, "admin-api", request.Reason, DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false);
                return result switch
                {
                    ForceRequeueJobResult.Requeued => Results.Ok(),
                    ForceRequeueJobResult.NotFound => Results.NotFound(),
                    _ => Results.Conflict(new { message = "ジョブが更新されています。最新状態を確認してください。" })
                };
            })
            .WithName("CancelCollectionTaskById")
            .WithSummary("Cancel a collection task with optimistic concurrency control");

        collectionApi.MapPost(
            "/tasks/{jobType}/{deduplicationKey}/requeue",
            async (
                string jobType,
                string deduplicationKey,
                IProcessingStateStore stateStore,
                CollectionExecutionTrigger executionTrigger,
                CancellationToken cancellationToken) =>
            {
                var success = await stateStore
                    .ForceRequeueJobAsync(jobType, deduplicationKey, DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false);
                if (success)
                {
                    executionTrigger.Signal();
                }

                return success ? Results.Ok() : Results.NotFound();
            })
            .WithName("RequeueCollectionTask")
            .WithSummary("Requeue a collection task");

        collectionApi.MapPost(
            "/result-days/{providerType}/{targetDate}/requeue",
            async (
                string providerType,
                DateOnly targetDate,
                ResultDayRequeueMode? mode,
                IProcessingStateStore stateStore,
                CollectionExecutionTrigger executionTrigger,
                CancellationToken cancellationToken) =>
            {
                var now = DateTimeOffset.UtcNow;
                var selectedMode = mode ?? ResultDayRequeueMode.Discovery;

                await stateStore.UpsertResultDayCollectionStatusAsync(
                    providerType,
                    targetDate,
                    ResultDayCollectionState.RetryScheduled,
                    expectedRaceCount: null,
                    completedRaceCount: null,
                    incompleteReason: null,
                    lastCompletedAt: null,
                    retryAfter: now,
                    lastError: null,
                    now,
                    cancellationToken).ConfigureAwait(false);

                if (selectedMode == ResultDayRequeueMode.Collection)
                {
                    var success = await stateStore.ForceRequeueJobAsync(
                        AgentJobType.ResultDayCollectionRequest,
                        AgentJobKeyFactory.BuildResultDayCollectionRequestKey(providerType, targetDate),
                        now,
                        cancellationToken).ConfigureAwait(false);
                    if (!success)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["mode"] = ["collection 再投入対象の日次収集ジョブが存在しません。discovery を利用してください。"]
                        });
                    }

                    executionTrigger.Signal();
                    return Results.Ok();
                }

                await stateStore.ScheduleJobAsync(
                    AgentJobType.ResultDayDiscoveryRequest,
                    AgentJobKeyFactory.BuildResultDayDiscoveryRequestKey(providerType, targetDate),
                    AgentJobPayloadSerializer.Serialize(new ResultDayDiscoveryRequestPayload(targetDate, providerType)),
                    now,
                    priority: 170,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                executionTrigger.Signal();

                return Results.Ok();
            })
            .WithName("RequeueCollectionResultDay")
            .WithSummary("Requeue result-day collection");

        collectionApi.MapPost(
            "/result-days/trigger",
            async (
                DateOnly targetDate,
                string? providerType,
                IProcessingStateStore stateStore,
                CollectionExecutionTrigger executionTrigger,
                CancellationToken cancellationToken) =>
            {
                var now = DateTimeOffset.UtcNow;
                var normalizedProviderType = string.IsNullOrWhiteSpace(providerType) ? "JRA" : providerType.Trim();

                await stateStore.UpsertResultDayCollectionStatusAsync(
                    normalizedProviderType,
                    targetDate,
                    ResultDayCollectionState.RetryScheduled,
                    expectedRaceCount: null,
                    completedRaceCount: null,
                    incompleteReason: null,
                    lastCompletedAt: null,
                    retryAfter: now,
                    lastError: null,
                    now,
                    cancellationToken).ConfigureAwait(false);

                await stateStore.ScheduleJobAsync(
                    AgentJobType.ResultDayDiscoveryRequest,
                    AgentJobKeyFactory.BuildResultDayDiscoveryRequestKey(normalizedProviderType, targetDate),
                    AgentJobPayloadSerializer.Serialize(new ResultDayDiscoveryRequestPayload(targetDate, normalizedProviderType)),
                    now,
                    priority: 200,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                executionTrigger.Signal();

                return Results.Ok(new
                {
                    providerType = normalizedProviderType,
                    targetDate,
                    queuedJobType = AgentJobType.ResultDayDiscoveryRequest
                });
            })
            .WithName("TriggerCollectionResultDay")
            .WithSummary("Trigger result-day collection");

        return endpoints;
    }

    private sealed record RequeueCollectionTaskRequest(DateTimeOffset ExpectedUpdatedAt, string? Reason = null);
    private sealed record CancelCollectionTaskRequest(DateTimeOffset ExpectedUpdatedAt, string Reason);
}
