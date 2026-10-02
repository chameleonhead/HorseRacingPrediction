using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionOperationsEndpointTests
{
    [TestMethod]
    public async Task Progress_ReturnsEveryLaneWithDueRunningAndLifecycleTimestamps()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            var startedAt = DateTimeOffset.UtcNow.AddMinutes(-3);
            var finishedAt = startedAt.AddMinutes(1);
            var completed = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "H-complete"),
                new("horse-profile"), 1, CollectionReason.Initial, startedAt.AddMinutes(-1),
                CollectionLane.Background, 10);
            var completedLease = await store.AcquireAsync(completed.TaskId!.Value, 1, startedAt,
                TimeSpan.FromMinutes(5));
            await store.CompleteAttemptAsync(completed.TaskId.Value, completedLease!.LeaseToken, finishedAt,
                new(CollectionAttemptResult.Succeeded));
            var running = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "H-running"),
                new("horse-profile"), 1, CollectionReason.Initial, startedAt,
                CollectionLane.Background, 10);
            await store.AcquireAsync(running.TaskId!.Value, 1, startedAt.AddMinutes(2), TimeSpan.FromMinutes(5));
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "H-due"), new("horse-profile"),
                1, CollectionReason.Initial, DateTimeOffset.UtcNow.AddMinutes(-1), CollectionLane.Normal, 30);
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "H-future"), new("horse-profile"),
                1, CollectionReason.Initial, DateTimeOffset.UtcNow.AddDays(1), CollectionLane.Realtime, 100);
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            var response = await client.GetFromJsonAsync<GetCollectionProgressResponse>(
                "/api/v2/admin/collection/operations/progress");

            Assert.IsNotNull(response);
            Assert.HasCount(3, response.Progress.LaneActivity);
            var realtime = response.Progress.LaneActivity.Single(x => x.Lane == CollectionLane.Realtime);
            var normal = response.Progress.LaneActivity.Single(x => x.Lane == CollectionLane.Normal);
            var background = response.Progress.LaneActivity.Single(x => x.Lane == CollectionLane.Background);
            Assert.AreEqual(0, realtime.DueReady);
            Assert.AreEqual(1, normal.DueReady);
            Assert.AreEqual(1, background.Running);
            Assert.AreEqual(startedAt.AddMinutes(2), background.LastStartedAt);
            Assert.AreEqual(finishedAt, background.LastCompletedAt);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task RacePeriodRecollection_ValidatesRangeWithoutCreatingTasks()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            using var reversed = await client.PostAsJsonAsync("/api/v2/admin/collection/recollection-batches",
                new CreateRecollectionBatchRequest(new CreateRecollectionBatchInputDto("RacePeriod", Provider: "JRA",
                    From: new(2026, 9, 13), To: new(2026, 9, 12))));
            using var tooLong = await client.PostAsJsonAsync("/api/v2/admin/collection/recollection-batches",
                new CreateRecollectionBatchRequest(new CreateRecollectionBatchInputDto("RacePeriod", Provider: "JRA",
                    From: new(2026, 7, 1), To: new(2026, 8, 1))));
            using var preview = await client.PostAsJsonAsync("/api/v2/admin/collection/recollection-previews",
                new PreviewRacePeriodRecollectionRequest(new PreviewRacePeriodRecollectionInputDto(
                    new(2026, 9, 12), new(2026, 9, 13), Provider: "jra")));
            using var reversedPreview = await client.PostAsJsonAsync("/api/v2/admin/collection/recollection-previews",
                new PreviewRacePeriodRecollectionRequest(new PreviewRacePeriodRecollectionInputDto(
                    new(2026, 9, 13), new(2026, 9, 12), Provider: "JRA")));

            Assert.AreEqual(HttpStatusCode.BadRequest, reversed.StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, tooLong.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, preview.StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, reversedPreview.StatusCode);
            var previewBody = await preview.Content.ReadFromJsonAsync<PreviewRacePeriodRecollectionResponse>();
            Assert.IsNotNull(previewBody);
            Assert.AreEqual(new DateOnly(2026, 9, 12), previewBody.Preview.From);
            Assert.AreEqual(new DateOnly(2026, 9, 13), previewBody.Preview.To);
            Assert.AreEqual(2, previewBody.Preview.InclusiveDays);
            Assert.AreEqual("JRA", previewBody.Preview.Provider);
            Assert.IsEmpty(await store.GetTasksAsync());
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task RacePeriodRecollection_IsInclusiveIdempotentPerBatch_AndRerunsTerminalDays()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();
            var request = new CreateRecollectionBatchRequest(new CreateRecollectionBatchInputDto("RacePeriod",
                Provider: "JRA", From: new(2026, 9, 12), To: new(2026, 9, 13), BatchId: "recollection:test-1"));

            using var firstResponse = await client.PostAsJsonAsync(
                "/api/v2/admin/collection/recollection-batches", request);
            var first = (await firstResponse.Content.ReadFromJsonAsync<CreateRecollectionBatchResponse>())?.Batch.RacePeriod;
            using var duplicateResponse = await client.PostAsJsonAsync(
                "/api/v2/admin/collection/recollection-batches", request);
            var duplicate = (await duplicateResponse.Content.ReadFromJsonAsync<CreateRecollectionBatchResponse>())?.Batch.RacePeriod;

            Assert.AreEqual(HttpStatusCode.Accepted, firstResponse.StatusCode);
            Assert.AreEqual(2, first!.Batch.ExpectedDiscoveryDays);
            Assert.AreEqual(2, first.TasksCreated);
            Assert.AreEqual(0, duplicate!.TasksCreated);
            Assert.AreEqual(2, duplicate.TasksReused);
            var firstTasks = await store.GetTasksAsync();
            Assert.HasCount(2, firstTasks);
            CollectionAssert.AreEquivalent(new[] { "recollection:20260912", "recollection:20260913" },
                firstTasks.Select(x => x.Resource.Id).ToArray());

            foreach (var task in firstTasks)
            {
                var lease = await store.AcquireAsync(task.TaskId, 1,
                    DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
                await store.CompleteAttemptAsync(task.TaskId, lease!.LeaseToken, DateTimeOffset.UtcNow,
                    new(CollectionAttemptResult.Succeeded));
            }

            using var rerunResponse = await client.PostAsJsonAsync(
                "/api/v2/admin/collection/recollection-batches",
                request with { Batch = request.Batch! with { BatchId = "recollection:test-2" } });
            var rerun = (await rerunResponse.Content.ReadFromJsonAsync<CreateRecollectionBatchResponse>())?.Batch.RacePeriod;

            Assert.AreEqual(HttpStatusCode.Accepted, rerunResponse.StatusCode);
            Assert.AreEqual(2, rerun!.TasksCreated);
            Assert.HasCount(4, await store.GetTasksAsync());
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task CreateRequest_RejectsFileUrl()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            using var response = await client.PostAsJsonAsync("/api/v2/admin/collection/tasks",
                new CreateCollectionTaskRequest(new CreateCollectionTaskInputDto("Resource",
                    Resource: new CollectionResourceTaskInputDto(CollectionResourceType.Horse, "JRA", "H123",
                        "horse-profile", 1, CollectionReason.ManualRefresh,
                        ExplicitUrl: "file:///JRADB/accessS.html"))));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "HTTP(S)");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task CreateRequest_ForSuppressedResource_ReturnsConflict()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            var resource = new ResourceKey(CollectionResourceType.Horse, "JRA", "merged-source");
            await store.SuppressResourceAsync(resource, "Merged horse was deleted", "repair-1",
                DateTimeOffset.UtcNow);
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            using var response = await client.PostAsJsonAsync("/api/v2/admin/collection/tasks",
                new CreateCollectionTaskRequest(new CreateCollectionTaskInputDto("Resource",
                    Resource: new CollectionResourceTaskInputDto(CollectionResourceType.Horse, "JRA", "merged-source",
                        "horse-profile", 1, CollectionReason.ManualRefresh))));

            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "補正済みのため収集対象外です");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task DeletedPublishedRoute_IsRejectedAndFailureRemainsUnpublished()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "published"),
                new("horse-profile"), 1, CollectionReason.Initial, now);
            var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
            var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
            await store.CompleteAttemptAsync(taskId, lease!.LeaseToken, now.AddSeconds(1),
                new(CollectionAttemptResult.PermanentFailure, "Broken"));
            var failure = (await store.GetActionableFailureNotificationsAsync(DateTimeOffset.UtcNow, 10)).Single();
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            using var publish = await client.PostAsJsonAsync(
                $"/api/admin/collection/failure-notifications/{failure.NotificationId}/published", new { });
            Assert.IsTrue(publish.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
            var actionableResponse = await client.GetFromJsonAsync<ListFailureNotificationsResponse>(
                "/api/v2/admin/collection/failure-notifications?view=Actionable");
            var unpublishedResponse = await client.GetFromJsonAsync<ListFailureNotificationsResponse>(
                "/api/v2/admin/collection/failure-notifications?view=Unpublished");
            var actionable = actionableResponse?.Notifications;
            var unpublished = unpublishedResponse?.Notifications;

            Assert.HasCount(1, actionable!);
            Assert.HasCount(1, unpublished!);
        }
        finally { Directory.Delete(directory, true); }
    }
    [TestMethod]
    public async Task TaskViewCounts_AggregateEveryStatusInOneResponse()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            var now = DateTimeOffset.UtcNow;
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "H001"), new("horse-profile"), 1,
                CollectionReason.Initial, now);
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "H002"), new("horse-profile"), 1,
                CollectionReason.Initial, now);
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            var response = await client.GetFromJsonAsync<GetTaskViewCountsResponse>(
                "/api/v2/admin/collection/operations/task-view-counts");
            var result = response?.Counts;

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Counts["waiting"]);
            Assert.AreEqual(2, result.Counts["all"]);
            Assert.AreEqual(0, result.Counts["attention"]);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task CreateRequest_RejectsUnknownOrContradictoryDiscriminatorPayloads()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            var missingMode = new
            {
                Task = new
                {
                    Resource = new
                    {
                        ResourceType = CollectionResourceType.Horse,
                        Provider = "JRA",
                        ResourceId = "H1",
                        DefinitionId = "horse-profile",
                        RequestedRevision = 1,
                        Reason = CollectionReason.ManualRefresh
                    }
                }
            };
            var unknownMode = new { Task = new { Mode = "Either", SourceUrl = new { Url = "https://example.test" } } };
            var contradictory = new
            {
                Task = new
                {
                    Mode = "Resource",
                    SourceUrl = new { Url = "https://example.test" },
                    Resource = new
                    {
                        ResourceType = CollectionResourceType.Horse,
                        Provider = "JRA",
                        ResourceId = "H1",
                        DefinitionId = "horse-profile",
                        RequestedRevision = 1,
                        Reason = CollectionReason.ManualRefresh
                    }
                }
            };

            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/api/v2/admin/collection/tasks", missingMode)).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/api/v2/admin/collection/tasks", unknownMode)).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/api/v2/admin/collection/tasks", contradictory)).StatusCode);
            Assert.IsEmpty(await store.GetTasksAsync());
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task TaskCancellation_UsesPatchBodyAndReturnsConflictForReplay()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "cancel-me"),
                new("horse-profile"), 1, CollectionReason.Initial, DateTimeOffset.UtcNow);
            var taskId = receipt.TaskId ?? throw new InvalidOperationException("Request did not create a task.");
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();
            var path = $"/api/v2/admin/collection/tasks/{taskId:D}";

            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.PatchAsJsonAsync(path,
                    new CancelCollectionTaskRequest(new CancelCollectionTaskInputDto(false)))).StatusCode);
            Assert.AreEqual(HttpStatusCode.NoContent,
                (await client.PatchAsJsonAsync(path,
                    new CancelCollectionTaskRequest(new CancelCollectionTaskInputDto(true)))).StatusCode);
            Assert.AreEqual(HttpStatusCode.Conflict,
                (await client.PatchAsJsonAsync(path,
                    new CancelCollectionTaskRequest(new CancelCollectionTaskInputDto(true)))).StatusCode);
            Assert.AreEqual(CollectionTaskStatus.Cancelled, (await store.GetTasksAsync()).Single().Status);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task TaskSearch_ReturnsFilteredPageAndRejectsUnknownStatus()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "H001"), new("horse-profile"), 1,
                CollectionReason.Initial, now, CollectionLane.Background, 10);
            await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", "H002"), new("horse-profile"), 1,
                CollectionReason.Initial, now.AddSeconds(1), CollectionLane.Normal, 50);
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            var pageResponse = await client.GetFromJsonAsync<ListCollectionTasksResponse>(
                "/api/v2/admin/collection/tasks?statuses=Ready&resourceType=Horse&provider=jra&page=1&pageSize=1");
            var page = pageResponse?.Page;

            Assert.IsNotNull(page);
            Assert.AreEqual(2, page.TotalCount);
            Assert.HasCount(1, page.Items);
            Assert.AreEqual("H002", page.Items[0].Resource.Id);
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.GetAsync("/api/v2/admin/collection/tasks?statuses=NoSuchStatus")).StatusCode);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task FailureGroups_AggregateSameCauseAndRecoveryCreatesNormalRequests()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            foreach (var id in new[] { "H001", "H002" })
            {
                var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", id),
                    new("horse-profile"), 1, CollectionReason.Initial, now,
                    explicitUrl: new Uri($"https://explicit.example.test/{id}"));
                var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
                Assert.IsTrue(await store.ReconcileDeadLetterAsync(taskId, 1, now.AddSeconds(1), "same failure"));
            }
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            var groupsResponse = await client.GetFromJsonAsync<ListFailureNotificationGroupsResponse>(
                "/api/v2/admin/collection/failure-notification-groups");
            var groups = groupsResponse?.Groups;
            Assert.IsNotNull(groups);
            Assert.HasCount(1, groups);
            Assert.AreEqual(2, groups[0].Count);
            Assert.HasCount(2, groups[0].NotificationIds);

            var response = await client.PostAsJsonAsync("/api/v2/admin/collection/recovery-batches",
                new CreateCollectionRecoveryBatchRequest(new CreateCollectionRecoveryBatchInputDto(
                    "NotificationIds", NotificationIds: groups[0].NotificationIds)));
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            var recoveryEnvelope = await response.Content.ReadFromJsonAsync<CreateCollectionRecoveryBatchResponse>();
            var recovery = recoveryEnvelope?.Recovery;
            Assert.IsNotNull(recovery);
            Assert.AreEqual(2, recovery.CreatedTaskCount);
            Assert.IsEmpty(await store.GetActionableFailureNotificationsAsync(DateTimeOffset.UtcNow, 10));
            Assert.AreEqual(2, (await store.GetTasksAsync()).Count(x => x.Status == CollectionTaskStatus.Ready));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task FailureGroupDetail_PagesSearchesAndRecoversEntireGroup()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            var now = DateTimeOffset.UtcNow.AddMinutes(-2);
            foreach (var id in new[] { "H001", "H002", "OTHER" })
            {
                var receipt = await store.RequestAsync(new(CollectionResourceType.Horse, "JRA", id),
                    new("horse-profile"), 1, CollectionReason.Initial, now,
                    explicitUrl: new Uri($"https://explicit.example.test/{id}"));
                var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
                var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
                await store.CompleteAttemptAsync(taskId, lease!.LeaseToken, now.AddSeconds(1),
                    new(CollectionAttemptResult.PermanentFailure, "PlaywrightException", "resource exhausted",
                        id == "H002" ? null : new Uri($"https://example.test/{id}"),
                        new Uri($"https://example.test/{id}/final"), 200,
                        "horse-profile"));
                await store.SetPausedAsync(false, null, now.AddSeconds(2));
            }
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();
            var groupsResponse = await client.GetFromJsonAsync<ListFailureNotificationGroupsResponse>(
                "/api/v2/admin/collection/failure-notification-groups");
            var group = groupsResponse!.Groups.Single();
            var key = group.GroupKey;
            var expectedNotificationIds = group.NotificationIds;

            var firstPageResponse = await client.GetFromJsonAsync<GetFailureNotificationGroupResponse>(
                $"/api/v2/admin/collection/failure-notification-groups/{key}?page=1&pageSize=2");
            var firstPage = firstPageResponse?.Page;
            var searchedResponse = await client.GetFromJsonAsync<GetFailureNotificationGroupResponse>(
                $"/api/v2/admin/collection/failure-notification-groups/{key}?search=H002&page=1&pageSize=50");
            var searched = searchedResponse?.Page;

            Assert.IsNotNull(firstPage);
            Assert.AreEqual(3, firstPage.TotalCount);
            Assert.HasCount(2, firstPage.Items);
            CollectionAssert.AreEquivalent(expectedNotificationIds.ToArray(),
                firstPage.Group.NotificationIds.ToArray());
            Assert.IsTrue(firstPage.Items.All(x => x.FinalUrl is not null));
            Assert.IsNotNull(searched);
            Assert.AreEqual(1, searched.TotalCount);
            CollectionAssert.AreEquivalent(expectedNotificationIds.ToArray(),
                searched.Group.NotificationIds.ToArray(),
                "Search and paging must not narrow the optimistic-concurrency membership snapshot.");
            Assert.AreEqual("H002", searched.Items.Single().Resource.Id);
            Assert.AreEqual("https://explicit.example.test/H002", searched.Items.Single().RequestedUrl);

            using var staleResponse = await client.PostAsJsonAsync("/api/v2/admin/collection/recovery-batches",
                new CreateCollectionRecoveryBatchRequest(new CreateCollectionRecoveryBatchInputDto("GroupKey",
                    GroupKey: key, ExpectedNotificationIds: Array.Empty<Guid>())));
            Assert.AreEqual(HttpStatusCode.Conflict, staleResponse.StatusCode,
                "Group recovery must reject membership drift between selection and execution.");
            var response = await client.PostAsJsonAsync("/api/v2/admin/collection/recovery-batches",
                new CreateCollectionRecoveryBatchRequest(new CreateCollectionRecoveryBatchInputDto("GroupKey",
                    GroupKey: key, ExpectedNotificationIds: expectedNotificationIds)));
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            var recoveryEnvelope = await response.Content.ReadFromJsonAsync<CreateCollectionRecoveryBatchResponse>();
            var recovery = recoveryEnvelope?.Recovery;
            Assert.IsNotNull(recovery);
            Assert.AreEqual(3, recovery.SelectedCount);
            Assert.AreEqual(3, recovery.CreatedTaskCount);
            Assert.AreEqual(HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/v2/admin/collection/failure-notification-groups/{key}")).StatusCode);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task FailureRecovery_AcceptsMoreThanOneThousandTargets()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();
            var response = await client.PostAsJsonAsync("/api/v2/admin/collection/recovery-batches",
                new CreateCollectionRecoveryBatchRequest(new CreateCollectionRecoveryBatchInputDto(
                    "NotificationIds", NotificationIds: Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToArray())));
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode,
                "The request must pass the size guard and fail only because the synthetic notifications do not exist.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task BackfillDetail_RecoversOnlyProjectedHoles()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var batch = await store.CreateOrResumeBackfillBatchAsync("2026-09", "JRA", new(2026, 9, 1), new(2026, 9, 1), now);
            var task = (await store.GetTasksAsync()).Single(x => x.Resource.Id == "backfill:20260901");
            Assert.IsTrue(await store.ReconcileDeadLetterAsync(task.TaskId, 1, now.AddSeconds(1), "failed"));
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            var detailResponse = await client.GetFromJsonAsync<GetBackfillBatchResponse>("/api/v2/admin/collection/backfill-batches/2026-09");
            var detail = detailResponse?.Batch;
            Assert.IsNotNull(detail);
            Assert.HasCount(1, detail.Holes);
            var response = await client.PostAsJsonAsync("/api/v2/admin/collection/backfill-batches/2026-09/recovery-batches", new { });
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            var resultEnvelope = await response.Content.ReadFromJsonAsync<RecoverBackfillHolesResponse>();
            var result = resultEnvelope?.Recovery;
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Holes);
            Assert.AreEqual(1, result.TasksCreated);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task BackfillRecovery_CanRetryAfterFailedRecovery_AndDoesNotDuplicateActiveTask()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
                1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            await store.CreateOrResumeBackfillBatchAsync("retryable", "JRA",
                new(2026, 9, 1), new(2026, 9, 1), now);
            var original = (await store.GetTasksAsync()).Single(x => x.Resource.Id == "backfill:20260901");
            Assert.IsTrue(await store.ReconcileDeadLetterAsync(original.TaskId, 1, now.AddSeconds(1), "failed"));
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            using var firstResponse = await client.PostAsJsonAsync(
                "/api/v2/admin/collection/backfill-batches/retryable/recovery-batches", new { });
            var first = (await firstResponse.Content.ReadFromJsonAsync<RecoverBackfillHolesResponse>())?.Recovery;
            Assert.AreEqual(1, first?.TasksCreated);
            using var duplicateResponse = await client.PostAsJsonAsync(
                "/api/v2/admin/collection/backfill-batches/retryable/recovery-batches", new { });
            var duplicate = (await duplicateResponse.Content.ReadFromJsonAsync<RecoverBackfillHolesResponse>())?.Recovery;
            Assert.AreEqual(0, duplicate?.TasksCreated, "An active task must not be duplicated.");

            var recovery = (await store.GetTasksAsync()).Single(x => x.TaskId != original.TaskId);
            Assert.IsTrue(await store.ReconcileDeadLetterAsync(recovery.TaskId, 1, now.AddSeconds(2), "again"));
            using var retryResponse = await client.PostAsJsonAsync(
                "/api/v2/admin/collection/backfill-batches/retryable/recovery-batches", new { });
            var retry = (await retryResponse.Content.ReadFromJsonAsync<RecoverBackfillHolesResponse>())?.Recovery;
            Assert.AreEqual(1, retry?.TasksCreated, "A terminal failed recovery must be retryable.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task BackfillRecovery_EmptyBatchIsNoOp()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
                1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            await store.CreateOrResumeBackfillBatchAsync("complete", "JRA",
                new(2026, 9, 1), new(2026, 9, 1), now);
            var task = (await store.GetTasksAsync()).Single(x => x.Resource.Id == "backfill:20260901");
            var lease = await store.AcquireAsync(task.TaskId, 1, now, TimeSpan.FromMinutes(5));
            Assert.IsNotNull(lease);
            await store.CompleteAttemptAsync(task.TaskId, lease.LeaseToken, now.AddSeconds(1),
                new(CollectionAttemptResult.Succeeded));
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            using var response = await client.PostAsJsonAsync(
                "/api/v2/admin/collection/backfill-batches/complete/recovery-batches", new { });
            var result = (await response.Content.ReadFromJsonAsync<RecoverBackfillHolesResponse>())?.Recovery;

            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            Assert.AreEqual(0, result?.Holes);
            Assert.AreEqual(0, result?.TasksCreated);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task BackfillRecovery_ExpandsAllHolesBeyondTypicalPageSize()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
                1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            await store.CreateOrResumeBackfillBatchAsync("large", "JRA",
                new(2026, 1, 1), new(2026, 4, 11), now);
            var tasks = (await store.GetTasksAsync(limit: 1000)).Where(x => x.Resource.Id.StartsWith("backfill:"))
                .ToList();
            Assert.HasCount(101, tasks);
            foreach (var task in tasks)
                Assert.IsTrue(await store.ReconcileDeadLetterAsync(task.TaskId, 1, now.AddSeconds(1), "failed"));
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            using var response = await client.PostAsJsonAsync(
                "/api/v2/admin/collection/backfill-batches/large/recovery-batches", new { });
            var result = (await response.Content.ReadFromJsonAsync<RecoverBackfillHolesResponse>())?.Recovery;

            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            Assert.AreEqual(101, result?.Holes);
            Assert.AreEqual(101, result?.TasksCreated);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"collection-operations-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task<CollectionPlatformStore> CreateStoreAsync(string directory)
    {
        var store = new CollectionPlatformStore(Options.Create(new CollectionPlatformOptions
        { StateDirectory = directory }));
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse", CollectionResourceType.Horse,
            1, "initial", false);
        await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", CollectionResourceType.Race,
            1, "initial", false);
        return store;
    }

    private static async Task<WebApplication> CreateApplicationAsync(CollectionPlatformStore store)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<ICollectionDispatchTelemetry, NullCollectionDispatchTelemetry>();
        var app = builder.Build();
        app.MapCollectionApiV2Endpoints();
        await app.StartAsync();
        return app;
    }
}
