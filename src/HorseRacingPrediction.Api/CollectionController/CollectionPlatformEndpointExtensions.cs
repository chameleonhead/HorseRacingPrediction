using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using EventFlow.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

namespace HorseRacingPrediction.Api.CollectionController;

public static class CollectionPlatformEndpointExtensions
{
    public static IEndpointRouteBuilder MapCollectionPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/collection").WithTags("Collection Platform");
        admin.MapGet("/tasks", async (CollectionTaskStatus? status, int? limit, CollectionPlatformStore store,
            CancellationToken token) => Results.Ok(await store.GetTasksAsync(status, limit ?? 200, token)));
        admin.MapGet("/tasks/search", async (string? statuses, ResourceType? resourceType, string? provider,
            string? definitionId, CollectionLane? lane, string? search, string? errorSearch, DateTimeOffset? createdFrom,
            DateTimeOffset? createdTo, int? page, int? pageSize, CollectionPlatformStore store,
            CancellationToken token) =>
        {
            IReadOnlyCollection<CollectionTaskStatus>? parsedStatuses = null;
            if (!string.IsNullOrWhiteSpace(statuses))
            {
                var values = new List<CollectionTaskStatus>();
                foreach (var value in statuses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!Enum.TryParse<CollectionTaskStatus>(value, true, out var parsed))
                        return Results.BadRequest(new { message = $"Unknown task status: {value}" });
                    values.Add(parsed);
                }
                parsedStatuses = values;
            }
            if (createdFrom > createdTo)
                return Results.BadRequest(new { message = "createdFrom must not be later than createdTo." });
            return Results.Ok(await store.SearchTasksAsync(new(parsedStatuses, resourceType, provider,
                definitionId, lane, search, createdFrom, createdTo, errorSearch, page ?? 1, pageSize ?? 50), token));
        });
        admin.MapGet("/states/search", async (string? statuses, ResourceType? resourceType, string? provider,
            string? definitionId, string? search, int? page, int? pageSize, CollectionPlatformStore store,
            CancellationToken token) =>
        {
            IReadOnlyCollection<CollectionStateStatus>? parsed = null;
            if (!string.IsNullOrWhiteSpace(statuses))
            {
                var values = new List<CollectionStateStatus>();
                foreach (var value in statuses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!Enum.TryParse<CollectionStateStatus>(value, true, out var status))
                        return Results.BadRequest(new { message = $"Unknown state status: {value}" });
                    values.Add(status);
                }
                parsed = values;
            }
            return Results.Ok(await store.SearchStatesAsync(new(parsed, resourceType, provider, definitionId,
                search, page ?? 1, pageSize ?? 50), token));
        });
        admin.MapGet("/progress", async (CollectionPlatformStore store, CancellationToken token) =>
            Results.Ok(await store.GetProgressAsync(token)));
        admin.MapGet("/dashboard", async (CollectionPlatformStore store, CancellationToken token) =>
        {
            var progressTask = store.GetProgressAsync(token);
            var notificationsTask = store.GetPendingFailureNotificationsAsync(DateTimeOffset.UtcNow, 10000, token);
            var backfillsTask = store.GetBackfillBatchesAsync(token);
            await Task.WhenAll(progressTask, notificationsTask, backfillsTask);
            return Results.Ok(new CollectionOperationsDashboard(await progressTask,
                BuildFailureGroups(await notificationsTask), await backfillsTask, DateTimeOffset.UtcNow));
        });
        admin.MapGet("/readiness/{raceId}", async (string raceId, CollectionPlatformStore store,
            CancellationToken token) => Results.Ok(await store.GetReadinessAsync(raceId, token)));
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
        admin.MapGet("/failure-notifications/groups", async (int? limit, CollectionPlatformStore store,
            CancellationToken token) =>
        {
            var notifications = await store.GetPendingFailureNotificationsAsync(DateTimeOffset.UtcNow,
                Math.Clamp(limit ?? 5000, 1, 10000), token);
            return Results.Ok(BuildFailureGroups(notifications));
        });
        admin.MapPost("/failure-notifications/recover", async (RecoverCollectionFailuresRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            var selectedIds = request.NotificationIds.Distinct().ToHashSet();
            if (selectedIds.Count == 0)
                return Results.BadRequest(new { message = "At least one notification is required." });
            if (selectedIds.Count > 10000)
                return Results.BadRequest(new { message = "At most 10000 notifications can be recovered at once." });
            var pending = await store.GetPendingFailureNotificationsAsync(DateTimeOffset.UtcNow, 10000, token);
            var selected = pending.Where(x => selectedIds.Contains(x.NotificationId)).ToList();
            if (selected.Count != selectedIds.Count)
                return Results.Conflict(new { message = "Some failures are no longer pending. Refresh and try again." });

            var taskIds = new List<Guid>(selected.Count);
            var created = 0;
            foreach (var failure in selected)
            {
                var state = await store.GetStateAsync(failure.Resource, failure.Definition, token);
                var requestedRevision = request.RequestedRevision ?? state?.RequiredRevision
                    ?? throw new InvalidOperationException("Collection state was not found for a pending failure.");
                var receipt = await store.RequestAsync(failure.Resource, failure.Definition,
                    requestedRevision, CollectionReason.Recovery,
                    DateTimeOffset.UtcNow, request.Lane, request.Priority, cancellationToken: token);
                if (receipt.CreatedTask) created++;
                taskIds.Add(receipt.TaskId);
                await store.MarkFailureNotificationPublishedAsync(failure.NotificationId,
                    DateTimeOffset.UtcNow, token);
            }
            return Results.Accepted(value: new CollectionFailureRecoveryResult(selected.Count, created,
                selected.Count - created, taskIds.Distinct().ToList()));
        });
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
        admin.MapGet("/resources/{type}/{provider}/{resourceId}/{definition}", async (
            ResourceType type, string provider, string resourceId, string definition,
            int? historyPage, int? requestHistoryPage, int? taskHistoryPage, int? attemptHistoryPage,
            int? historyPageSize,
            CollectionPlatformStore store, CancellationToken token) =>
            await store.GetResourceDetailPagedAsync(new(type, provider, resourceId), new(definition),
                requestHistoryPage ?? historyPage ?? 1, taskHistoryPage ?? historyPage ?? 1,
                attemptHistoryPage ?? historyPage ?? 1,
                historyPageSize ?? 25, token) is { } detail
                ? Results.Ok(detail) : Results.NotFound());
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
        admin.MapPost("/requests/by-url", async (CreateExplicitUrlCollectionRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            var identified = JraExplicitUrlResolver.Resolve(request.Url);
            if (!identified.Identified || identified.Resource is null || identified.Definition is null
                || identified.EffectiveDate is null || identified.ExplicitUrl is null)
                return Results.Json(identified, statusCode: StatusCodes.Status422UnprocessableEntity);
            var explicitUrl = new Uri(identified.ExplicitUrl);
            var resource = identified.Resource.Value;
            var definition = identified.Definition.Value;
            var lane = CollectionLane.Realtime;
            var priority = resource.Type switch
            {
                ResourceType.RaceOdds => (int)CollectionPriority.High,
                ResourceType.RaceResult => (int)CollectionPriority.Critical,
                ResourceType.RaceCard => 80,
                _ => (int)CollectionPriority.Normal,
            };
            var currentRevision = await store.GetCurrentRevisionAsync(definition, token);
            var receipt = await store.RequestAsync(resource, definition, currentRevision, CollectionReason.ManualRefresh,
                DateTimeOffset.UtcNow, lane, priority, explicitUrl, effectiveDate: identified.EffectiveDate,
                attributes: identified.Attributes, cancellationToken: token);
            return Results.Accepted($"/api/admin/collection/tasks/{receipt.TaskId}", identified with { Receipt = receipt });
        });
        admin.MapPost("/requests/bulk/preview", async (BulkCollectionOperationRequest request,
            CollectionPlatformStore store, [FromServices] IDbContextProvider<EventStoreDbContext> domain,
            IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
        {
            var targets = await ResolveBulkTargetsAsync(request, store, domain, conditions, token);
            return Results.Ok(await store.PreviewBulkRequestAsync(new(request.DefinitionId),
                request.RequestedRevision, targets, token));
        });
        admin.MapPost("/requests/bulk", async (BulkCollectionOperationRequest request,
            CollectionPlatformStore store, [FromServices] IDbContextProvider<EventStoreDbContext> domain,
            IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) =>
        {
            var targets = await ResolveBulkTargetsAsync(request, store, domain, conditions, token);
            var actual = targets.Select(x => x.Resource.Normalize()).Distinct().OrderBy(x => x.ToString()).ToArray();
            var expected = (request.ExpectedResources ?? []).Select(x => x.Normalize()).Distinct()
                .OrderBy(x => x.ToString()).ToArray();
            if (expected.Length == 0 || !actual.SequenceEqual(expected))
                return Results.Conflict(new { message = "Selection changed after preview; preview again before executing." });
            var batchId = string.IsNullOrWhiteSpace(request.BatchId) ? $"manual:{Guid.NewGuid():N}" : request.BatchId;
            return Results.Accepted(value: await store.ExecuteBulkRequestAsync(new(request.DefinitionId),
                request.RequestedRevision, request.Reason, targets, DateTimeOffset.UtcNow, batchId,
                request.Lane, request.Priority, token));
        });
        admin.MapPost("/revisions/preview", async (RevisionImpactPreviewRequest request,
            CollectionPlatformStore store, IEnumerable<INamedRevisionImpactCondition> conditions,
            CancellationToken token) => Results.Ok(await store.PreviewRevisionImpactAsync(
                new(request.DefinitionId), request.Revision, BuildImpact(request.Impact), conditions, token)));
        admin.MapPost("/revisions/apply", async (ApplyCollectionRevisionRequest request,
            CollectionPlatformStore store, IEnumerable<INamedRevisionImpactCondition> conditions,
            CancellationToken token) =>
        {
            var impact = BuildImpact(request.Impact);
            var affected = await store.AddRevisionAndApplyImpactAsync(new(request.DefinitionId), request.Revision,
                request.Description, impact, conditions, DateTimeOffset.UtcNow, token);
            return Results.Ok(new CollectionRevisionApplyResult(request.DefinitionId, request.Revision, affected));
        });
        admin.MapPost("/revisions/{definition}/{revision:int}/recollect", async (string definition, int revision,
            RevisionRecollectionRequest request, CollectionPlatformStore store,
            IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) => Results.Accepted(
            value: await store.ExpandRevisionRecollectionAsync(new(definition), revision, conditions,
                DateTimeOffset.UtcNow, request.Lane, request.Priority, token)));
        admin.MapGet("/revisions/{definition}/{revision:int}/progress", async (string definition, int revision,
            CollectionPlatformStore store, IEnumerable<INamedRevisionImpactCondition> conditions,
            CancellationToken token) => Results.Ok(await store.GetRevisionRecollectionProgressAsync(
                new(definition), revision, conditions, token)));
        admin.MapPost("/backfills", async (CreateBackfillBatchRequest request, CollectionPlatformStore store,
            CancellationToken token) =>
        {
            if (request.Month is < 1 or > 12 || request.Year is < 1900 or > 2200)
                return Results.BadRequest(new { message = "Year and month are invalid." });
            var from = new DateOnly(request.Year, request.Month, 1);
            var to = from.AddMonths(1).AddDays(-1);
            var batchId = string.IsNullOrWhiteSpace(request.BatchId)
                ? $"{request.Provider.Trim().ToLowerInvariant()}:{request.Year:D4}-{request.Month:D2}"
                : request.BatchId;
            var batch = await store.CreateOrResumeBackfillBatchAsync(batchId, request.Provider,
                from, to, DateTimeOffset.UtcNow, token);
            return Results.Accepted($"/api/admin/collection/backfills/{Uri.EscapeDataString(batchId)}", batch);
        });
        admin.MapGet("/backfills", async (CollectionPlatformStore store, CancellationToken token) =>
            Results.Ok(await store.GetBackfillBatchesAsync(token)));
        admin.MapGet("/backfills/{batchId}", async (string batchId, CollectionPlatformStore store,
            CancellationToken token) => await store.GetBackfillBatchAsync(batchId, token) is { } batch
                ? Results.Ok(batch) : Results.NotFound());
        admin.MapPost("/backfills/{batchId}/recover-holes", async (string batchId, CollectionPlatformStore store,
            CancellationToken token) =>
        {
            var batch = await store.GetBackfillBatchAsync(batchId, token);
            if (batch is null) return Results.NotFound();
            var created = 0;
            foreach (var hole in batch.Holes)
            {
                var state = await store.GetStateAsync(hole.Resource, hole.Definition, token);
                var receipt = await store.RequestAsync(hole.Resource, hole.Definition,
                    Math.Max(1, state?.RequiredRevision ?? state?.AppliedRevision ?? 1), CollectionReason.Recovery,
                    DateTimeOffset.UtcNow, CollectionLane.Background, (int)CollectionPriority.Background,
                    batchId: $"recovery:{batchId}", cancellationToken: token);
                if (receipt.CreatedTask) created++;
            }
            return Results.Accepted(value: new BackfillHoleRecoveryResult(batch.Holes.Count, created));
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
                    request.HttpStatusCode, request.PageIdentification, request.RetryAt, request.NextCollectionAt,
                    request.LocationOutcomes), token);
            return accepted ? Results.NoContent() : Results.Conflict();
        });
        worker.MapPost("/tasks/{taskId:guid}/heartbeat", async (Guid taskId,
            HeartbeatCollectionTaskRequest request, CollectionPlatformStore store, CancellationToken token) =>
            await store.HeartbeatAsync(taskId, request.LeaseToken, DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(Math.Clamp(request.LeaseSeconds, 30, 3600)), token)
                ? Results.NoContent() : Results.Conflict());
        return endpoints;
    }

    private static RevisionImpact BuildImpact(RevisionImpactRequest request) => request.ScopeType switch
    {
        RevisionImpactScopeType.All => new(request.ScopeType, string.Empty),
        RevisionImpactScopeType.SpecificResources when request.Resources is { Count: > 0 } =>
            new(request.ScopeType, System.Text.Json.JsonSerializer.Serialize(request.Resources)),
        RevisionImpactScopeType.DateRange when request.From is not null && request.To is not null
                                               && request.From <= request.To =>
            new(request.ScopeType, System.Text.Json.JsonSerializer.Serialize(new
                { From = request.From.Value, To = request.To.Value })),
        RevisionImpactScopeType.NamedCondition when !string.IsNullOrWhiteSpace(request.NamedCondition) =>
            new(request.ScopeType, request.NamedCondition),
        _ => throw new ArgumentException("Revision impact parameters are invalid."),
    };

    private static string CreateFailureGroupKey(string definition, CollectionTaskStatus status, string errorCode)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{definition}\n{status}\n{errorCode}"));
        return Convert.ToHexString(bytes, 0, 8);
    }

    private static IReadOnlyList<CollectionFailureGroup> BuildFailureGroups(
        IReadOnlyList<PendingCollectionFailureNotification> notifications) => notifications.GroupBy(x => new
        {
            Definition = x.Definition.Value, x.Status, ErrorCode = x.ErrorCode ?? string.Empty,
        }).Select(x => new CollectionFailureGroup(
            CreateFailureGroupKey(x.Key.Definition, x.Key.Status, x.Key.ErrorCode), new(x.Key.Definition), x.Key.Status,
            string.IsNullOrEmpty(x.Key.ErrorCode) ? null : x.Key.ErrorCode,
            x.OrderByDescending(y => y.FailedAt).Select(y => y.ErrorMessage).FirstOrDefault(), x.Count(),
            x.Min(y => y.FailedAt), x.Max(y => y.FailedAt), x.Select(y => y.NotificationId).ToList(),
            x.Select(y => y.Resource).Distinct().Take(20).ToList())).OrderByDescending(x => x.LastFailedAt).ToList();

    private static async Task<IReadOnlyList<CollectionBulkTarget>> ResolveBulkTargetsAsync(
        BulkCollectionOperationRequest request, CollectionPlatformStore store,
        IDbContextProvider<EventStoreDbContext> domainProvider,
        IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token)
    {
        if (request.Selection == BulkCollectionSelection.SpecificResources)
            return (request.Resources ?? []).Select(x => new CollectionBulkTarget(x)).ToList();
        if (request.Selection == BulkCollectionSelection.LastCollectedBefore)
            return await store.SelectBulkTargetsAsync(new(request.DefinitionId), request.LastCollectedBefore,
                cancellationToken: token);
        if (request.Selection is BulkCollectionSelection.Failed or BulkCollectionSelection.Stale)
            return await store.SelectBulkTargetsAsync(new(request.DefinitionId), status:
                request.Selection == BulkCollectionSelection.Failed ? CollectionStateStatus.Failed : CollectionStateStatus.Stale,
                cancellationToken: token);
        if (request.Selection == BulkCollectionSelection.RevisionImpact)
            return await store.GetRevisionImpactTargetsAsync(new(request.DefinitionId),
                request.ImpactRevision ?? throw new ArgumentException("ImpactRevision is required."), conditions, token);

        await using var db = domainProvider.CreateContext();
        var contexts = await db.RacePredictionContexts.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
        IEnumerable<RacePredictionContextReadModel> selected = contexts;
        if (request.Selection == BulkCollectionSelection.HorsesRacedInDateRange)
        {
            if (request.From is null || request.To is null || request.From > request.To)
                throw new ArgumentException("A valid From/To range is required.");
            selected = contexts.Where(x => x.RaceDate >= request.From && x.RaceDate <= request.To);
        }
        else if (request.Selection == BulkCollectionSelection.HorsesByTrainer)
        {
            if (string.IsNullOrWhiteSpace(request.TrainerId)) throw new ArgumentException("TrainerId is required.");
            selected = contexts.Where(x => x.Entries.Any(e => string.Equals(e.TrainerId, request.TrainerId,
                StringComparison.Ordinal)));
        }
        else throw new ArgumentOutOfRangeException(nameof(request.Selection));
        return selected.SelectMany(x => x.Entries)
            .Where(x => request.Selection != BulkCollectionSelection.HorsesByTrainer
                        || string.Equals(x.TrainerId, request.TrainerId, StringComparison.Ordinal))
            .Select(x => x.HorseId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)
            .Select(x => new CollectionBulkTarget(new(ResourceType.Horse, request.Provider, x))).ToList();
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
public sealed record RecoverCollectionFailuresRequest(IReadOnlyList<Guid> NotificationIds,
    int? RequestedRevision = null, CollectionLane Lane = CollectionLane.Normal,
    int Priority = (int)CollectionPriority.Normal);

public enum BulkCollectionSelection
{
    SpecificResources, HorsesRacedInDateRange, HorsesByTrainer, LastCollectedBefore,
    RevisionImpact, Failed, Stale,
}

public sealed record BulkCollectionOperationRequest(string DefinitionId, int RequestedRevision,
    CollectionReason Reason, BulkCollectionSelection Selection, string Provider = "JRA",
    IReadOnlyList<ResourceKey>? Resources = null, DateOnly? From = null, DateOnly? To = null,
    string? TrainerId = null, DateTimeOffset? LastCollectedBefore = null, int? ImpactRevision = null,
    IReadOnlyList<ResourceKey>? ExpectedResources = null, string? BatchId = null,
    CollectionLane Lane = CollectionLane.Background, int Priority = (int)CollectionPriority.Background);

public sealed record RevisionImpactRequest(RevisionImpactScopeType ScopeType,
    IReadOnlyList<ResourceKey>? Resources = null, DateOnly? From = null, DateOnly? To = null,
    string? NamedCondition = null);
public sealed record RevisionImpactPreviewRequest(string DefinitionId, int Revision, RevisionImpactRequest Impact);
public sealed record ApplyCollectionRevisionRequest(string DefinitionId, int Revision, string Description,
    RevisionImpactRequest Impact);
public sealed record CollectionRevisionApplyResult(string DefinitionId, int Revision, int Affected);
public sealed record BackfillHoleRecoveryResult(int Holes, int TasksCreated);
public sealed record CollectionOperationsDashboard(CollectionProgressSnapshot Progress,
    IReadOnlyList<CollectionFailureGroup> Failures, IReadOnlyList<BackfillBatchSnapshot> Backfills,
    DateTimeOffset GeneratedAt);
public sealed record RevisionRecollectionRequest(CollectionLane Lane = CollectionLane.Background,
    int Priority = (int)CollectionPriority.Background);
public sealed record CreateBackfillBatchRequest(int Year, int Month, string Provider = "JRA", string? BatchId = null);

public sealed record CompleteCollectionAttemptRequest(string LeaseToken, CollectionAttemptResult Result,
    string? ErrorCode = null, string? ErrorMessage = null, string? RequestedUrl = null,
    string? FinalUrl = null, int? HttpStatusCode = null, string? PageIdentification = null,
    DateTimeOffset? RetryAt = null, DateTimeOffset? NextCollectionAt = null,
    IReadOnlyList<ResourceLocationOutcome>? LocationOutcomes = null);
