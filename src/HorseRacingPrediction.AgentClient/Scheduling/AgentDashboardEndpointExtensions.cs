namespace HorseRacingPrediction.AgentClient.Scheduling;

public static class AgentDashboardEndpointExtensions
{
    public static IEndpointRouteBuilder MapAgentDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/agent/dashboard",
            () => Results.Content(AgentDashboardHtmlRenderer.Render(), "text/html; charset=utf-8"));

        endpoints.MapGet(
            "/agent/job-statuses",
            async (
                string? jobType,
                AgentJobStatus? status,
                int? limit,
                ProcessingStateStore stateStore,
                CancellationToken cancellationToken) =>
            {
                var items = await stateStore
                    .GetJobStatusesAsync(jobType, status, limit ?? 100, cancellationToken)
                    .ConfigureAwait(false);
                return Results.Ok(items);
            });

        endpoints.MapGet(
            "/agent/result-day-statuses",
            async (DateOnly from, DateOnly to, ProcessingStateStore stateStore, CancellationToken cancellationToken) =>
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
            });

        endpoints.MapPost(
            "/agent/job-statuses/{jobType}/{deduplicationKey}/requeue",
            async (
                string jobType,
                string deduplicationKey,
                ProcessingStateStore stateStore,
                CancellationToken cancellationToken) =>
            {
                var success = await stateStore
                    .ForceRequeueJobAsync(jobType, deduplicationKey, DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false);
                return success ? Results.Ok() : Results.NotFound();
            });

        endpoints.MapPost(
            "/agent/result-day-statuses/{providerType}/{targetDate}/requeue",
            async (
                string providerType,
                DateOnly targetDate,
                ResultDayRequeueMode? mode,
                ProcessingStateStore stateStore,
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

                    return Results.Ok();
                }

                await stateStore.ScheduleJobAsync(
                    AgentJobType.ResultDayDiscoveryRequest,
                    AgentJobKeyFactory.BuildResultDayDiscoveryRequestKey(providerType, targetDate),
                    AgentJobPayloadSerializer.Serialize(new ResultDayDiscoveryRequestPayload(targetDate, providerType)),
                    now,
                    priority: 170,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                return Results.Ok();
            });

        endpoints.MapPost(
            "/agent/result-day-jobs/trigger",
            async (
                DateOnly targetDate,
                string? providerType,
                ProcessingStateStore stateStore,
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

                return Results.Ok(new
                {
                    providerType = normalizedProviderType,
                    targetDate,
                    queuedJobType = AgentJobType.ResultDayDiscoveryRequest
                });
            });

        endpoints.MapPost(
            "/agent/prediction-jobs/trigger",
            async (
                string raceId,
                ProcessingStateStore stateStore,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(raceId))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["raceId"] = ["raceId は必須です。"]
                    });
                }

                var normalizedRaceId = raceId.Trim();
                var now = DateTimeOffset.UtcNow;
                await stateStore
                    .EnqueuePredictionCandidatesAsync([normalizedRaceId], now, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new
                {
                    raceId = normalizedRaceId,
                    queuedJobType = AgentJobType.PredictionExecution
                });
            });

        return endpoints;
    }
}
