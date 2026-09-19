using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using EventFlow.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Shared = HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api.CollectionController;

public static class CollectionPlatformEndpointExtensions
{
    public static IEndpointRouteBuilder MapCollectionPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/collection").WithTags("Collection Platform");
        admin.MapGet("/tasks", async (CollectionTaskStatus? status, int? limit, CollectionPlatformStore store,
            CancellationToken token) => Results.Ok(await store.GetTasksAsync(status, limit ?? 200, token)));
        admin.MapGet("/execution-batches/{executionBatchId:guid}", async (Guid executionBatchId,
            CollectionPlatformStore store, CancellationToken token) =>
            await store.GetExecutionBatchAsync(executionBatchId, token) is { } batch
                ? Results.Ok(batch) : Results.NotFound());
        admin.MapGet("/tasks/search", async (string? statuses, ResourceType? resourceType, string? provider,
            string? definitionId, CollectionLane? lane, string? search, string? errorSearch, DateTimeOffset? createdFrom,
            DateTimeOffset? createdTo, bool? actionableOnly, bool? latestOnly, int? page, int? pageSize, CollectionPlatformStore store,
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
                definitionId, lane, search, createdFrom, createdTo, errorSearch, page ?? 1, pageSize ?? 50,
                actionableOnly ?? false, latestOnly ?? false), token));
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
        admin.MapGet("/task-view-counts", async (CollectionPlatformStore store, CancellationToken token) =>
            Results.Ok(await store.GetTaskViewCountsAsync(token)));
        admin.MapGet("/dashboard", async (CollectionPlatformStore store, CancellationToken token) =>
        {
            var progressTask = store.GetProgressAsync(token);
            var notificationsTask = store.GetActionableFailureNotificationsAsync(HorseRacingPrediction.Contracts.Time.JstTime.Now(), 10000, token);
            var backfillsTask = store.GetBackfillBatchesAsync(token);
            await Task.WhenAll(progressTask, notificationsTask, backfillsTask);
            return Results.Ok(new CollectionOperationsDashboard(await progressTask,
                CollectionFailureGrouping.Build(await notificationsTask), await backfillsTask, HorseRacingPrediction.Contracts.Time.JstTime.Now()));
        });
        admin.MapGet("/readiness/{raceId}", async (string raceId, CollectionPlatformStore store,
            CancellationToken token) => Results.Ok(await store.GetReadinessAsync(raceId, token)));
        admin.MapGet("/pipeline", async (CollectionPlatformStore store, CancellationToken token) =>
            Results.Ok(await store.GetPipelineStateAsync(token)));
        admin.MapPost("/pipeline/pause", async (PauseCollectionPipelineRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            await store.SetPausedAsync(true, request.Reason, HorseRacingPrediction.Contracts.Time.JstTime.Now(), token);
            return Results.NoContent();
        });
        admin.MapPost("/pipeline/resume", async (CollectionPlatformStore store, CancellationToken token) =>
        {
            await store.SetPausedAsync(false, null, HorseRacingPrediction.Contracts.Time.JstTime.Now(), token);
            return Results.NoContent();
        });
        admin.MapPost("/migrations/race-detail/preview", async (CollectionPlatformStore store,
            CancellationToken token) => Results.Ok(await store.MergeLegacyRaceDetailsAsync(false,
                HorseRacingPrediction.Contracts.Time.JstTime.Now(), token)));
        admin.MapPost("/migrations/race-detail/apply", async (CollectionPlatformStore store,
            CancellationToken token) =>
        {
            var pipeline = await store.GetPipelineStateAsync(token);
            if (!pipeline.IsPaused) return Results.Conflict(new { message = "Collection pipeline must be paused." });
            var report = await store.MergeLegacyRaceDetailsAsync(true,
                HorseRacingPrediction.Contracts.Time.JstTime.Now(), token);
            return report.Errors.Count == 0 ? Results.Ok(report) : Results.Conflict(report);
        });
        admin.MapPost("/migrations/race-entry-owners/preview", async (
            [FromServices] IDbContextProvider<EventStoreDbContext> dbContextProvider,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            var candidates = await GetRaceEntryOwnerMigrationCandidatesAsync(dbContextProvider, token);
            var batch = await store.GetBatchResourceStatusesAsync(RaceEntryOwnerMigrationBatchId, token);
            return Results.Ok(BuildRaceEntryOwnerMigrationProgress(candidates, batch));
        });
        admin.MapPost("/migrations/race-entry-owners/apply", async (
            [FromServices] IDbContextProvider<EventStoreDbContext> dbContextProvider,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            var pipeline = await store.GetPipelineStateAsync(token);
            if (!pipeline.IsPaused)
                return Results.Conflict(new { message = "Collection pipeline must be paused." });
            if ((await store.GetTasksAsync(CollectionTaskStatus.Running, 1, token)).Count > 0)
                return Results.Conflict(new { message = "Running collection tasks must be drained before migration." });
            var candidates = await GetRaceEntryOwnerMigrationCandidatesAsync(dbContextProvider, token);
            var actionable = candidates.Where(x => x.Eligibility == RaceEntryOwnerRepairEligibility.CardRetrievalCandidate)
                .ToArray();
            if (actionable.Length > 0)
                await store.ExecuteBulkRequestAsync(new("race-detail"), 2, CollectionReason.DefinitionChanged,
                    actionable.Select(ToRaceEntryOwnerMigrationTarget),
                    HorseRacingPrediction.Contracts.Time.JstTime.Now(), RaceEntryOwnerMigrationBatchId,
                    CollectionLane.Normal, (int)CollectionPriority.Normal, token).ConfigureAwait(false);
            var batch = await store.GetBatchResourceStatusesAsync(RaceEntryOwnerMigrationBatchId, token);
            return Results.Ok(BuildRaceEntryOwnerMigrationProgress(candidates, batch));
        });
        admin.MapGet("/migrations/race-entry-owners/progress", async (
            [FromServices] IDbContextProvider<EventStoreDbContext> dbContextProvider,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            var candidates = await GetRaceEntryOwnerMigrationCandidatesAsync(dbContextProvider, token);
            var batch = await store.GetBatchResourceStatusesAsync(RaceEntryOwnerMigrationBatchId, token);
            return Results.Ok(BuildRaceEntryOwnerMigrationProgress(candidates, batch));
        });
        admin.MapPost("/tasks/{taskId:guid}/cancel", async (Guid taskId, CollectionPlatformStore store,
            CancellationToken token) => await store.CancelTaskAsync(taskId, HorseRacingPrediction.Contracts.Time.JstTime.Now(), token)
                ? Results.NoContent() : Results.Conflict());
        admin.MapPost("/maintenance/obsolete-subject-profile-tasks", async (
            ObsoleteSubjectProfileCleanupRequest request, CollectionPlatformStore store,
            [FromServices] IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken token) =>
        {
            var candidates = await store.GetObsoleteSubjectProfileTasksAsync(token).ConfigureAwait(false);
            if (candidates.Count == 0)
                return Results.Ok(new ObsoleteSubjectProfileTaskCleanupResult(request.Execute, 0, 0, 0, []));
            using var db = dbContextProvider.CreateContext();
            var raceIds = candidates.Select(item => item.RequestedByRaceId!).Distinct(StringComparer.Ordinal).ToArray();
            var races = await db.Set<RacePredictionContextReadModel>().AsNoTracking()
                .Where(item => raceIds.Contains(item.RaceId)).ToListAsync(token).ConfigureAwait(false);
            var raceById = races.ToDictionary(item => item.RaceId, StringComparer.Ordinal);
            var horseIds = candidates.Where(item => item.Resource.Type == ResourceType.Horse)
                .Select(item => item.Resource.Id).Distinct(StringComparer.Ordinal).ToArray();
            var jockeyIds = candidates.Where(item => item.Resource.Type == ResourceType.Jockey)
                .Select(item => item.Resource.Id).Distinct(StringComparer.Ordinal).ToArray();
            var trainerIds = candidates.Where(item => item.Resource.Type == ResourceType.Trainer)
                .Select(item => item.Resource.Id).Distinct(StringComparer.Ordinal).ToArray();
            var existingHorses = (await db.Set<HorseReadModel>().AsNoTracking()
                .Where(item => horseIds.Contains(item.HorseId)).Select(item => item.HorseId).ToArrayAsync(token))
                .ToHashSet(StringComparer.Ordinal);
            var existingJockeys = (await db.Set<JockeyReadModel>().AsNoTracking()
                .Where(item => jockeyIds.Contains(item.JockeyId)).Select(item => item.JockeyId).ToArrayAsync(token))
                .ToHashSet(StringComparer.Ordinal);
            var existingTrainers = (await db.Set<TrainerReadModel>().AsNoTracking()
                .Where(item => trainerIds.Contains(item.TrainerId)).Select(item => item.TrainerId).ToArrayAsync(token))
                .ToHashSet(StringComparer.Ordinal);
            bool IsExisting(ObsoleteSubjectProfileTask item) => item.Resource.Type switch
            {
                ResourceType.Horse => existingHorses.Contains(item.Resource.Id),
                ResourceType.Jockey => existingJockeys.Contains(item.Resource.Id),
                ResourceType.Trainer => existingTrainers.Contains(item.Resource.Id),
                _ => false,
            };
            bool IsReferenced(ObsoleteSubjectProfileTask item)
            {
                if (!raceById.TryGetValue(item.RequestedByRaceId!, out var race)) return false;
                return item.Resource.Type switch
                {
                    ResourceType.Horse => race.Entries.Any(entry => entry.HorseId == item.Resource.Id),
                    ResourceType.Jockey => race.Entries.Any(entry => entry.JockeyId == item.Resource.Id),
                    ResourceType.Trainer => race.Entries.Any(entry => entry.TrainerId == item.Resource.Id),
                    _ => false,
                };
            }
            var obsolete = candidates.Where(item => !IsExisting(item) || !IsReferenced(item)).ToArray();
            if (!request.Execute)
                return Results.Ok(new ObsoleteSubjectProfileTaskCleanupResult(false, obsolete.Length, 0, 0, obsolete));
            return Results.Ok(await store.RetireObsoleteSubjectProfileTasksAsync(
                obsolete.Select(item => item.TaskId).ToArray(), Shared.Time.JstTime.Now(), token)
                .ConfigureAwait(false));
        });
        admin.MapGet("/failure-notifications", async (int? limit, CollectionPlatformStore store,
            CancellationToken token) => Results.Ok(await store.GetActionableFailureNotificationsAsync(
                HorseRacingPrediction.Contracts.Time.JstTime.Now(), Math.Clamp(limit ?? 100, 1, 1000), token)));
        admin.MapGet("/failure-notifications/unpublished", async (int? limit, CollectionPlatformStore store,
            CancellationToken token) => Results.Ok(await store.GetUnpublishedFailureNotificationsAsync(
                HorseRacingPrediction.Contracts.Time.JstTime.Now(), Math.Clamp(limit ?? 100, 1, 1000), token)));
        admin.MapGet("/failure-notifications/groups", async (int? limit, CollectionPlatformStore store,
            CancellationToken token) =>
        {
            var notifications = await store.GetActionableFailureNotificationsAsync(HorseRacingPrediction.Contracts.Time.JstTime.Now(),
                Math.Clamp(limit ?? 5000, 1, 10000), token);
            return Results.Ok(CollectionFailureGrouping.Build(notifications));
        });
        admin.MapGet("/failure-notifications/groups/{groupKey}", async (string groupKey, string? search,
            int? page, int? pageSize, CollectionPlatformStore store, CancellationToken token) =>
        {
            try
            {
                var result = await store.GetActionableFailureGroupPageAsync(groupKey, HorseRacingPrediction.Contracts.Time.JstTime.Now(),
                    search, page ?? 1, pageSize ?? 50, token);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { message = exception.Message });
            }
        });
        admin.MapPost("/failure-notifications/recover", async (RecoverCollectionFailuresRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            var selectedIds = request.NotificationIds.Distinct().ToHashSet();
            if (selectedIds.Count == 0)
                return Results.BadRequest(new { message = "At least one notification is required." });
            if (selectedIds.Count > 10000)
                return Results.BadRequest(new { message = "At most 10000 notifications can be recovered at once." });
            var pending = await store.GetActionableFailureNotificationsAsync(HorseRacingPrediction.Contracts.Time.JstTime.Now(), 10000, token);
            var selected = pending.Where(x => selectedIds.Contains(x.NotificationId)).ToList();
            if (selected.Count != selectedIds.Count)
                return Results.Conflict(new { message = "Some failures are no longer pending. Refresh and try again." });
            return await RecoverFailuresAsync(selected, request.RequestedRevision, request.Lane,
                request.Priority, store, token);
        });
        admin.MapPost("/failure-notifications/groups/{groupKey}/recover", async (string groupKey,
            RecoverCollectionFailureGroupRequest request, CollectionPlatformStore store, CancellationToken token) =>
        {
            var match = await store.GetActionableFailureGroupAsync(groupKey, HorseRacingPrediction.Contracts.Time.JstTime.Now(), token);
            if (match.MatchingGroupCount == 0) return Results.NotFound();
            if (match.MatchingGroupCount > 1)
                return Results.Conflict(new { message = "The failure group key matches multiple groups." });
            if (match.Notifications.Count > 10000)
                return Results.BadRequest(new { message = "At most 10000 notifications can be recovered at once." });
            return await RecoverFailuresAsync(match.Notifications, request.RequestedRevision, request.Lane,
                request.Priority, store, token);
        });
        admin.MapPost("/failure-notifications/{notificationId:guid}/published", async (Guid notificationId,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            await store.MarkFailureNotificationPublishedAsync(notificationId, HorseRacingPrediction.Contracts.Time.JstTime.Now(), token);
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
            if (!CollectionHttpUrl.TryCreate(request.ExplicitUrl, out var explicitUrl)
                && request.ExplicitUrl is not null)
                return Results.BadRequest(new { message = "ExplicitUrl must be an absolute HTTP(S) URL." });
            try
            {
                var receipt = await store.RequestAsync(new(request.ResourceType, request.Provider, request.ResourceId),
                    new(request.DefinitionId), request.RequestedRevision, request.Reason, HorseRacingPrediction.Contracts.Time.JstTime.Now(),
                    request.Lane, request.Priority, explicitUrl, request.BatchId, request.EffectiveDate,
                    request.Attributes, token);
                return Results.Accepted($"/api/admin/collection/tasks/{receipt.TaskId}", receipt);
            }
            catch (CollectionResourceSuppressedException)
            {
                return Results.Conflict(new { message = "補正済みのため収集対象外です。" });
            }
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
            try
            {
                var receipt = await store.RequestAsync(resource, definition, currentRevision, CollectionReason.ManualRefresh,
                    HorseRacingPrediction.Contracts.Time.JstTime.Now(), lane, priority, explicitUrl, effectiveDate: identified.EffectiveDate,
                    attributes: identified.Attributes, cancellationToken: token);
                return Results.Accepted($"/api/admin/collection/tasks/{receipt.TaskId}", identified with { Receipt = receipt });
            }
            catch (CollectionResourceSuppressedException)
            {
                return Results.Conflict(new { message = "補正済みのため収集対象外です。" });
            }
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
                request.RequestedRevision, request.Reason, targets, HorseRacingPrediction.Contracts.Time.JstTime.Now(), batchId,
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
                request.Description, impact, conditions, HorseRacingPrediction.Contracts.Time.JstTime.Now(), token);
            return Results.Ok(new CollectionRevisionApplyResult(request.DefinitionId, request.Revision, affected));
        });
        admin.MapPost("/revisions/{definition}/{revision:int}/recollect", async (string definition, int revision,
            RevisionRecollectionRequest request, CollectionPlatformStore store,
            IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token) => Results.Accepted(
            value: await store.ExpandRevisionRecollectionAsync(new(definition), revision, conditions,
                HorseRacingPrediction.Contracts.Time.JstTime.Now(), request.Lane, request.Priority, token)));
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
                from, to, HorseRacingPrediction.Contracts.Time.JstTime.Now(), token);
            return Results.Accepted($"/api/admin/collection/backfills/{Uri.EscapeDataString(batchId)}", batch);
        });
        admin.MapPost("/requests/batch", async (Shared.CollectionRequestBulkRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            if (request.Items is null || request.Items.Count is < 1 or > 500)
                return Results.BadRequest(new { message = "Batch items must contain between 1 and 500 entries." });
            if (request.Items.Select(item => item.ItemKey).Distinct(StringComparer.Ordinal).Count() != request.Items.Count)
                return Results.BadRequest(new { message = "Batch item keys must be unique." });
            var items = new List<CollectionRequestBatchItem>(request.Items.Count);
            foreach (var item in request.Items)
            {
                if (!Enum.TryParse<ResourceType>(item.ResourceType, true, out var resourceType)
                    || !Enum.TryParse<CollectionReason>(item.Reason, true, out var reason)
                    || !Enum.TryParse<CollectionLane>(item.Lane, true, out var lane))
                    return Results.BadRequest(new { message = $"Invalid enum value for item {item.ItemKey}." });
                if (!CollectionHttpUrl.TryCreate(item.ExplicitUrl, out var explicitUrl) && item.ExplicitUrl is not null)
                    return Results.BadRequest(new { message = $"ExplicitUrl is invalid for item {item.ItemKey}." });
                items.Add(new(item.ItemKey, new(resourceType, item.Provider, item.ResourceId),
                    new(item.DefinitionId), item.RequestedRevision, reason, lane, item.Priority, explicitUrl,
                    item.EffectiveDate, item.Attributes));
            }
            var outcomes = await store.RequestManyAsync(request.BatchId, items,
                HorseRacingPrediction.Contracts.Time.JstTime.Now(), token);
            return Results.Ok(new Shared.CollectionRequestBulkResponse(outcomes.Select(outcome =>
                new Shared.CollectionRequestBulkOutcome(outcome.ItemKey, outcome.Status,
                    outcome.Receipt?.RequestId, outcome.Receipt?.TaskId,
                    outcome.Receipt?.CreatedTask ?? false, outcome.ErrorCode, outcome.Message)).ToArray()));
        });
        admin.MapPost("/race-period-recollections/preview", (CreateRacePeriodRecollectionRequest request) =>
        {
            var validation = ValidateRacePeriodRecollection(request);
            return validation is null
                ? Results.Ok(new RacePeriodRecollectionPreview(request.From, request.To,
                    request.To.DayNumber - request.From.DayNumber + 1, request.Provider.Trim().ToUpperInvariant()))
                : Results.BadRequest(new { message = validation });
        });
        admin.MapPost("/race-period-recollections", async (CreateRacePeriodRecollectionRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            var validation = ValidateRacePeriodRecollection(request);
            if (validation is not null) return Results.BadRequest(new { message = validation });
            var batchId = string.IsNullOrWhiteSpace(request.BatchId)
                ? $"recollection:{request.From:yyyyMMdd}-{request.To:yyyyMMdd}:{Guid.NewGuid():N}"
                : request.BatchId.Trim();
            var receipt = await store.CreateOrResumeRacePeriodRecollectionAsync(batchId, request.Provider,
                request.From, request.To, HorseRacingPrediction.Contracts.Time.JstTime.Now(), token);
            return Results.Accepted($"/api/admin/collection/backfills/{Uri.EscapeDataString(batchId)}", receipt);
        });
        admin.MapGet("/repairs/race-entry-owners/preview", async (DateOnly date,
            [FromServices] IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken token) =>
        {
            using var db = dbContextProvider.CreateContext();
            var races = await db.Set<RacePredictionContextReadModel>().AsNoTracking()
                .Where(x => x.RaceDate == date)
                .OrderBy(x => x.RacecourseCode).ThenBy(x => x.RaceNumber)
                .ToListAsync(token).ConfigureAwait(false);
            var candidates = races.Select(ToRaceEntryOwnerRepairCandidate)
                .Where(x => x.MissingOwnerCount > 0
                    && x.Eligibility == RaceEntryOwnerRepairEligibility.CardRetrievalCandidate).ToArray();
            return Results.Ok(new RaceEntryOwnerRepairPreview(date, races.Count, candidates));
        });
        admin.MapPost("/repairs/race-entry-owners", async (RaceEntryOwnerRepairRequest request,
            [FromServices] IDbContextProvider<EventStoreDbContext> dbContextProvider, CollectionPlatformStore store,
            CancellationToken token) =>
        {
            var pipeline = await store.GetPipelineStateAsync(token);
            if (!pipeline.IsPaused)
                return Results.Conflict(new { message = "Use the paused race entry owner migration." });
            if ((await store.GetTasksAsync(CollectionTaskStatus.Running, 1, token)).Count > 0)
                return Results.Conflict(new { message = "Running collection tasks must be drained before migration." });
            var selected = request.RaceIds.Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (selected.Length is 0 or > 24)
                return Results.BadRequest(new { message = "Select between 1 and 24 races." });
            using var db = dbContextProvider.CreateContext();
            var races = await db.Set<RacePredictionContextReadModel>().AsNoTracking()
                .Where(x => x.RaceDate == request.Date && selected.Contains(x.RaceId))
                .ToListAsync(token).ConfigureAwait(false);
            var candidates = races.Select(ToRaceEntryOwnerRepairCandidate)
                .Where(x => x.MissingOwnerCount > 0).ToArray();
            if (candidates.Length != selected.Length)
                return Results.Conflict(new { message = "Selection changed after preview; preview again before executing." });
            var targets = candidates.Select(x => new CollectionBulkTarget(
                new(ResourceType.Race, "JRA", x.ResourceId), request.Date,
                new Dictionary<string, string>
                {
                    ["domainRaceId"] = x.RaceId,
                    ["date"] = request.Date.ToString("yyyy-MM-dd"),
                    ["course"] = x.RacecourseCode,
                    ["number"] = x.RaceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["ownerRepair"] = "true",
                })).ToArray();
            var batchId = RaceEntryOwnerMigrationBatchId;
            var result = await store.ExecuteBulkRequestAsync(new("race-detail"), 2,
                CollectionReason.DefinitionChanged, targets, HorseRacingPrediction.Contracts.Time.JstTime.Now(),
                batchId, CollectionLane.Normal, (int)CollectionPriority.Normal, token).ConfigureAwait(false);
            return Results.Accepted(value: new RaceEntryOwnerRepairReceipt(batchId, result.TargetCount,
                result.TasksCreated, result.Requests.Select(x => x.TaskId).ToArray()));
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
            var recoveryBatchId = $"recovery:{batchId}:{Guid.NewGuid():N}";
            foreach (var hole in batch.Holes)
            {
                var state = await store.GetStateAsync(hole.Resource, hole.Definition, token);
                var receipt = await store.RequestAsync(hole.Resource, hole.Definition,
                    Math.Max(1, state?.RequiredRevision ?? state?.AppliedRevision ?? 1), CollectionReason.Recovery,
                    HorseRacingPrediction.Contracts.Time.JstTime.Now(), CollectionLane.Background, (int)CollectionPriority.Background,
                    batchId: recoveryBatchId, cancellationToken: token);
                if (receipt.CreatedTask) created++;
            }
            return Results.Accepted(value: new BackfillHoleRecoveryResult(batch.Holes.Count, created));
        });

        var worker = endpoints.MapGroup("/api/internal/collection").WithTags("Collection Worker");
        worker.MapPost("/executions/acquire-next", async (CollectionExecutionAcquireRequest request,
            CollectionPlatformStore store, CancellationToken token) => Results.Ok(
                await store.AcquireNextExecutionAsync(request.Wake, request.QueueMessageId,
                    HorseRacingPrediction.Contracts.Time.JstTime.Now(), TimeSpan.FromSeconds(45), token)));
        worker.MapPost("/executions/{executionBatchId:guid}/start", async (Guid executionBatchId,
            CollectionExecutionStartRequest request, CollectionPlatformStore store, CancellationToken token) =>
            await store.StartExecutionAsync(executionBatchId, request,
                HorseRacingPrediction.Contracts.Time.JstTime.Now(), token)
                ? Results.NoContent() : Results.Conflict());
        worker.MapPost("/executions/{executionBatchId:guid}/complete", async (Guid executionBatchId,
            CollectionExecutionCompleteRequest request, CollectionPlatformStore store, CancellationToken token) =>
            await store.CompleteExecutionAsync(executionBatchId, request.LeaseToken,
                HorseRacingPrediction.Contracts.Time.JstTime.Now(), token)
                ? Results.NoContent() : Results.Conflict());
        worker.MapPost("/tasks/{taskId:guid}/acquire", async (Guid taskId, AcquireCollectionTaskRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            if (request.Correlation is not null && !request.Correlation.IsSupported())
                return Results.BadRequest(new { Error = "The collection attempt correlation is invalid." });
            var lease = await store.AcquireAsync(taskId, request.DispatchGeneration, HorseRacingPrediction.Contracts.Time.JstTime.Now(),
                TimeSpan.FromSeconds(Math.Clamp(request.LeaseSeconds, 30, 3600)), request.Correlation, token);
            if (lease is not null)
                return Results.Ok(new CollectionTaskAcquireResult(CollectionTaskAcquireStatus.Acquired, lease));
            var status = await store.ClassifyAcquireFailureAsync(taskId, request.DispatchGeneration, token);
            return Results.Ok(new CollectionTaskAcquireResult(status));
        });
        worker.MapPost("/tasks/{taskId:guid}/complete", async (Guid taskId, CompleteCollectionAttemptRequest request,
            CollectionPlatformStore store, CancellationToken token) =>
        {
            Uri.TryCreate(request.RequestedUrl, UriKind.Absolute, out var requestedUrl);
            Uri.TryCreate(request.FinalUrl, UriKind.Absolute, out var finalUrl);
            var accepted = await store.CompleteAttemptAsync(taskId, request.LeaseToken, HorseRacingPrediction.Contracts.Time.JstTime.Now(),
                new(request.Result, request.ErrorCode, request.ErrorMessage, requestedUrl, finalUrl,
                    request.HttpStatusCode, request.PageIdentification, request.RetryAt, request.NextCollectionAt,
                    request.LocationOutcomes, request.FailureImpact), token);
            return accepted ? Results.NoContent() : Results.Conflict();
        });
        worker.MapPost("/tasks/{taskId:guid}/heartbeat", async (Guid taskId,
            HeartbeatCollectionTaskRequest request, CollectionPlatformStore store, CancellationToken token) =>
            await store.HeartbeatAsync(taskId, request.LeaseToken, HorseRacingPrediction.Contracts.Time.JstTime.Now(),
                TimeSpan.FromSeconds(Math.Clamp(request.LeaseSeconds, 30, 3600)), token)
                ? Results.NoContent() : Results.Conflict());
        return endpoints;
    }

    private sealed record ObsoleteSubjectProfileCleanupRequest(bool Execute = false);

    private static RaceEntryOwnerRepairCandidate ToRaceEntryOwnerRepairCandidate(
        RacePredictionContextReadModel race)
    {
        var date = race.RaceDate ?? throw new InvalidOperationException("Race date is required.");
        var number = race.RaceNumber ?? throw new InvalidOperationException("Race number is required.");
        var course = CanonicalRaceCourse(race.RacecourseCode);
        var cardLookupStart = HorseRacingPrediction.Contracts.Time.JstTime.Today()
            .AddDays(-HorseRacingPrediction.Contracts.JraCollectionPolicy.DefaultRaceCardLookupPeriodDays);
        var eligibility = date >= cardLookupStart
            ? RaceEntryOwnerRepairEligibility.CardRetrievalCandidate
            : RaceEntryOwnerRepairEligibility.OutsideCardLookupPeriod;
        var reason = eligibility == RaceEntryOwnerRepairEligibility.CardRetrievalCandidate
            ? "出馬表の探索対象期間内です。実際の公開有無は収集時に確認します。"
            : $"出馬表の探索対象期間（{cardLookupStart:yyyy-MM-dd}以降）外のため、現行の公式取得元から補正できません。";
        return new(race.RaceId, $"{date:yyyyMMdd}:{course}:{number}", race.RaceName,
            race.RacecourseCode ?? course, number, race.Entries.Count,
            race.Entries.Count(x => string.IsNullOrWhiteSpace(x.OwnerName)), date, eligibility, reason);
    }

    private const string RaceEntryOwnerMigrationBatchId = "migration:race-entry-owners:v2";
    private static async Task<IReadOnlyList<RaceEntryOwnerRepairCandidate>> GetRaceEntryOwnerMigrationCandidatesAsync(
        IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken token)
    {
        using var db = dbContextProvider.CreateContext();
        var races = await db.Set<RacePredictionContextReadModel>().AsNoTracking()
            .OrderBy(x => x.RaceDate).ThenBy(x => x.RacecourseCode).ThenBy(x => x.RaceNumber)
            .ToListAsync(token).ConfigureAwait(false);
        return races.Where(x => x.RaceDate.HasValue && x.RaceNumber.HasValue && x.Entries.Count > 0)
            .Select(ToRaceEntryOwnerRepairCandidate).Where(x => x.MissingOwnerCount > 0).ToArray();
    }

    private static CollectionBulkTarget ToRaceEntryOwnerMigrationTarget(RaceEntryOwnerRepairCandidate candidate) =>
        new(new(ResourceType.Race, "JRA", candidate.ResourceId), candidate.Date,
            new Dictionary<string, string>
            {
                ["domainRaceId"] = candidate.RaceId,
                ["date"] = candidate.Date.ToString("yyyy-MM-dd"),
                ["course"] = candidate.RacecourseCode,
                ["number"] = candidate.RaceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ownerRepair"] = "true",
            });

    private static RaceEntryOwnerMigrationProgress BuildRaceEntryOwnerMigrationProgress(
        IReadOnlyList<RaceEntryOwnerRepairCandidate> missing,
        IReadOnlyList<CollectionBatchResourceStatus> batch)
    {
        var missingIds = missing.Select(x => x.ResourceId).ToHashSet(StringComparer.Ordinal);
        var batchIds = batch.Select(x => x.Resource.Id).ToHashSet(StringComparer.Ordinal);
        var classified = missing.Select(x => batchIds.Contains(x.ResourceId)
            ? x with
            {
                Eligibility = RaceEntryOwnerRepairEligibility.ExistingRequest,
                EligibilityReason = "同じmigration batchの補正要求が既に存在します。"
            }
            : x).ToArray();
        var corrected = batch.Count(x => !missingIds.Contains(x.Resource.Id));
        var processing = batch.Count(x => missingIds.Contains(x.Resource.Id)
            && x.StateStatus != CollectionStateStatus.Unavailable
            && x.LatestTaskStatus is CollectionTaskStatus.Pending or CollectionTaskStatus.Ready
                or CollectionTaskStatus.Running or CollectionTaskStatus.RetryWaiting
                or CollectionTaskStatus.WaitingDiscovery);
        var failed = batch.Count(x => missingIds.Contains(x.Resource.Id)
            && x.StateStatus != CollectionStateStatus.Unavailable
            && x.LatestTaskStatus is CollectionTaskStatus.Failed or CollectionTaskStatus.DeadLetter
                or CollectionTaskStatus.Cancelled);
        var outsideWindow = classified.Count(x => x.Eligibility == RaceEntryOwnerRepairEligibility.OutsideCardLookupPeriod);
        var unavailable = outsideWindow + batch.Count(x => missingIds.Contains(x.Resource.Id)
            && x.StateStatus == CollectionStateStatus.Unavailable);
        var actionable = classified.Count(x => x.Eligibility == RaceEntryOwnerRepairEligibility.CardRetrievalCandidate);
        return new(RaceEntryOwnerMigrationBatchId, batch.Count, corrected, processing, failed, unavailable,
            missing.Count, classified, actionable, outsideWindow);
    }

    private static string CanonicalRaceCourse(string? value) => value?.Trim() switch
    {
        "札幌" or "Sapporo" => "Sapporo",
        "函館" or "Hakodate" => "Hakodate",
        "福島" or "Fukushima" => "Fukushima",
        "新潟" or "Niigata" => "Niigata",
        "東京" or "Tokyo" => "Tokyo",
        "中山" or "Nakayama" => "Nakayama",
        "中京" or "Chukyo" => "Chukyo",
        "京都" or "Kyoto" => "Kyoto",
        "阪神" or "Hanshin" => "Hanshin",
        "小倉" or "Kokura" => "Kokura",
        _ => throw new InvalidOperationException($"Unsupported JRA racecourse '{value}'.")
    };

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

    private static async Task<IResult> RecoverFailuresAsync(
        IReadOnlyList<PendingCollectionFailureNotification> failures, int? requestedRevision,
        CollectionLane lane, int priority, CollectionPlatformStore store, CancellationToken token)
    {
        var taskIds = new List<Guid>(failures.Count);
        var created = 0;
        foreach (var failure in failures)
        {
            var state = await store.GetStateAsync(failure.Resource, failure.Definition, token);
            var revision = requestedRevision ?? state?.RequiredRevision
                ?? throw new InvalidOperationException("Collection state was not found for a pending failure.");
            var receipt = await store.RequestAsync(failure.Resource, failure.Definition,
                revision, CollectionReason.Recovery, HorseRacingPrediction.Contracts.Time.JstTime.Now(), lane, priority,
                cancellationToken: token);
            if (receipt.CreatedTask) created++;
            taskIds.Add(receipt.TaskId);
        }
        return Results.Accepted(value: new CollectionFailureRecoveryResult(failures.Count, created,
            failures.Count - created, taskIds.Distinct().ToList()));
    }

    private static async Task<IReadOnlyList<CollectionBulkTarget>> ResolveBulkTargetsAsync(
        BulkCollectionOperationRequest request, CollectionPlatformStore store,
        IDbContextProvider<EventStoreDbContext> domainProvider,
        IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token)
    {
        IReadOnlyList<CollectionBulkTarget> targets;
        if (request.Selection == BulkCollectionSelection.SpecificResources)
            targets = (request.Resources ?? []).Select(x => new CollectionBulkTarget(x)).ToList();
        else if (request.Selection == BulkCollectionSelection.LastCollectedBefore)
            targets = await store.SelectBulkTargetsAsync(new(request.DefinitionId), request.LastCollectedBefore,
                cancellationToken: token).ConfigureAwait(false);
        else if (request.Selection is BulkCollectionSelection.Failed or BulkCollectionSelection.Stale)
            targets = await store.SelectBulkTargetsAsync(new(request.DefinitionId), status:
                request.Selection == BulkCollectionSelection.Failed ? CollectionStateStatus.Failed : CollectionStateStatus.Stale,
                cancellationToken: token).ConfigureAwait(false);
        else if (request.Selection == BulkCollectionSelection.RevisionImpact)
            targets = await store.GetRevisionImpactTargetsAsync(new(request.DefinitionId),
                request.ImpactRevision ?? throw new ArgumentException("ImpactRevision is required."), conditions, token)
                .ConfigureAwait(false);
        else
        {
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
            targets = selected.SelectMany(x => x.Entries)
                .Where(x => request.Selection != BulkCollectionSelection.HorsesByTrainer
                            || string.Equals(x.TrainerId, request.TrainerId, StringComparison.Ordinal))
                .Select(x => x.HorseId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)
                .Select(x => new CollectionBulkTarget(new(ResourceType.Horse, request.Provider, x))).ToList();
        }
        return await store.ExcludeSuppressedResourcesAsync(targets, token).ConfigureAwait(false);
    }

    private static string? ValidateRacePeriodRecollection(CreateRacePeriodRecollectionRequest request)
    {
        if (!string.Equals(request.Provider?.Trim(), "JRA", StringComparison.OrdinalIgnoreCase))
            return "Provider must be JRA.";
        if (request.From > request.To) return "開始日は終了日以前にしてください。";
        if (request.To > HorseRacingPrediction.Contracts.Time.JstTime.Today())
            return "未来日のレースは再取得できません。";
        if (request.To.DayNumber - request.From.DayNumber + 1 > 31)
            return "期間は31日以内にしてください。";
        return null;
    }
}

public sealed record CreateCollectionRequest(ResourceType ResourceType, string Provider, string ResourceId,
    string DefinitionId, int RequestedRevision, CollectionReason Reason,
    CollectionLane Lane = CollectionLane.Normal, int Priority = (int)CollectionPriority.Normal,
    string? ExplicitUrl = null, string? BatchId = null, DateOnly? EffectiveDate = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

public sealed record AcquireCollectionTaskRequest(long DispatchGeneration, int LeaseSeconds = 900,
    CollectionAttemptCorrelation? Correlation = null);
public sealed record HeartbeatCollectionTaskRequest(string LeaseToken, int LeaseSeconds = 900);
public sealed record PauseCollectionPipelineRequest(string? Reason);
public sealed record RecoverCollectionFailuresRequest(IReadOnlyList<Guid> NotificationIds,
    int? RequestedRevision = null, CollectionLane Lane = CollectionLane.Normal,
    int Priority = (int)CollectionPriority.Normal);
public sealed record RecoverCollectionFailureGroupRequest(int? RequestedRevision = null,
    CollectionLane Lane = CollectionLane.Normal, int Priority = (int)CollectionPriority.Normal);

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
public sealed record CreateRacePeriodRecollectionRequest(DateOnly From, DateOnly To, string Provider = "JRA",
    string? BatchId = null);
public enum RaceEntryOwnerRepairEligibility
{
    CardRetrievalCandidate,
    ExistingRequest,
    OutsideCardLookupPeriod,
}
public sealed record RaceEntryOwnerRepairCandidate(string RaceId, string ResourceId, string? RaceName,
    string RacecourseCode, int RaceNumber, int EntryCount, int MissingOwnerCount, DateOnly Date = default,
    RaceEntryOwnerRepairEligibility Eligibility = RaceEntryOwnerRepairEligibility.CardRetrievalCandidate,
    string? EligibilityReason = null);
public sealed record RaceEntryOwnerRepairPreview(DateOnly Date, int RaceCount,
    IReadOnlyList<RaceEntryOwnerRepairCandidate> Candidates);
public sealed record RaceEntryOwnerRepairRequest(DateOnly Date, IReadOnlyList<string> RaceIds,
    string? BatchId = null);
public sealed record RaceEntryOwnerRepairReceipt(string BatchId, int TargetCount, int TasksCreated,
    IReadOnlyList<Guid> TaskIds);
public sealed record RaceEntryOwnerMigrationProgress(string BatchId, int Requested, int Corrected,
    int Processing, int Failed, int Unavailable, int Remaining,
    IReadOnlyList<RaceEntryOwnerRepairCandidate> Candidates, int Eligible = 0,
    int OutsideCardLookupPeriod = 0);

public sealed record CompleteCollectionAttemptRequest(string LeaseToken, CollectionAttemptResult Result,
    string? ErrorCode = null, string? ErrorMessage = null, string? RequestedUrl = null,
    string? FinalUrl = null, int? HttpStatusCode = null, string? PageIdentification = null,
    DateTimeOffset? RetryAt = null, DateTimeOffset? NextCollectionAt = null,
    IReadOnlyList<ResourceLocationOutcome>? LocationOutcomes = null,
    CollectionFailureImpact FailureImpact = CollectionFailureImpact.StopPipeline);
