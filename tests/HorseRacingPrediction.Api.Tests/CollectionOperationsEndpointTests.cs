using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionOperationsEndpointTests
{
    [TestMethod]
    public async Task TaskViewCounts_AggregateEveryStatusInOneResponse()
    {
        var directory = CreateDirectory();
        try
        {
            var store = await CreateStoreAsync(directory);
            var now = DateTimeOffset.UtcNow;
            await store.RequestAsync(new(ResourceType.Horse, "JRA", "H001"), new("horse-profile"), 1,
                CollectionReason.Initial, now);
            await store.RequestAsync(new(ResourceType.Horse, "JRA", "H002"), new("horse-profile"), 1,
                CollectionReason.Initial, now);
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            var result = await client.GetFromJsonAsync<CollectionTaskViewCounts>(
                "/api/admin/collection/task-view-counts");

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Counts["waiting"]);
            Assert.AreEqual(2, result.Counts["all"]);
            Assert.AreEqual(0, result.Counts["attention"]);
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
            await store.RequestAsync(new(ResourceType.Horse, "JRA", "H001"), new("horse-profile"), 1,
                CollectionReason.Initial, now, CollectionLane.Background, 10);
            await store.RequestAsync(new(ResourceType.Horse, "JRA", "H002"), new("horse-profile"), 1,
                CollectionReason.Initial, now.AddSeconds(1), CollectionLane.Normal, 50);
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            var page = await client.GetFromJsonAsync<CollectionTaskPage>(
                "/api/admin/collection/tasks/search?statuses=Ready&resourceType=Horse&provider=jra&page=1&pageSize=1");

            Assert.IsNotNull(page);
            Assert.AreEqual(2, page.TotalCount);
            Assert.HasCount(1, page.Items);
            Assert.AreEqual("H002", page.Items[0].Resource.Id);
            Assert.AreEqual(HttpStatusCode.BadRequest,
                (await client.GetAsync("/api/admin/collection/tasks/search?statuses=NoSuchStatus")).StatusCode);
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
                var receipt = await store.RequestAsync(new(ResourceType.Horse, "JRA", id),
                    new("horse-profile"), 1, CollectionReason.Initial, now);
                Assert.IsTrue(await store.ReconcileDeadLetterAsync(receipt.TaskId, 1, now.AddSeconds(1), "same failure"));
            }
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            var groups = await client.GetFromJsonAsync<IReadOnlyList<CollectionFailureGroup>>(
                "/api/admin/collection/failure-notifications/groups");
            Assert.IsNotNull(groups);
            Assert.HasCount(1, groups);
            Assert.AreEqual(2, groups[0].Count);
            Assert.HasCount(2, groups[0].NotificationIds);

            var response = await client.PostAsJsonAsync("/api/admin/collection/failure-notifications/recover",
                new RecoverCollectionFailuresRequest(groups[0].NotificationIds));
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            var recovery = await response.Content.ReadFromJsonAsync<CollectionFailureRecoveryResult>();
            Assert.IsNotNull(recovery);
            Assert.AreEqual(2, recovery.CreatedTaskCount);
            Assert.IsEmpty(await store.GetPendingFailureNotificationsAsync(DateTimeOffset.UtcNow, 10));
            Assert.AreEqual(2, (await store.GetTasksAsync()).Count(x => x.Status == CollectionTaskStatus.Ready));
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
            var response = await client.PostAsJsonAsync("/api/admin/collection/failure-notifications/recover",
                new RecoverCollectionFailuresRequest(Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToArray()));
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
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", ResourceType.Race, 1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var batch = await store.CreateOrResumeBackfillBatchAsync("2026-09", "JRA", new(2026, 9, 1), new(2026, 9, 1), now);
            var task = (await store.GetTasksAsync()).Single(x => x.Resource.Id == "backfill:20260901");
            Assert.IsTrue(await store.ReconcileDeadLetterAsync(task.TaskId, 1, now.AddSeconds(1), "failed"));
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            var detail = await client.GetFromJsonAsync<BackfillBatchSnapshot>("/api/admin/collection/backfills/2026-09");
            Assert.IsNotNull(detail);
            Assert.HasCount(1, detail.Holes);
            var response = await client.PostAsJsonAsync("/api/admin/collection/backfills/2026-09/recover-holes", new { });
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<BackfillHoleRecoveryResult>();
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
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", ResourceType.Race,
                1, "initial", false);
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            await store.CreateOrResumeBackfillBatchAsync("retryable", "JRA",
                new(2026, 9, 1), new(2026, 9, 1), now);
            var original = (await store.GetTasksAsync()).Single(x => x.Resource.Id == "backfill:20260901");
            Assert.IsTrue(await store.ReconcileDeadLetterAsync(original.TaskId, 1, now.AddSeconds(1), "failed"));
            await using var app = await CreateApplicationAsync(store);
            using var client = app.GetTestClient();

            using var firstResponse = await client.PostAsJsonAsync(
                "/api/admin/collection/backfills/retryable/recover-holes", new { });
            var first = await firstResponse.Content.ReadFromJsonAsync<BackfillHoleRecoveryResult>();
            Assert.AreEqual(1, first?.TasksCreated);
            using var duplicateResponse = await client.PostAsJsonAsync(
                "/api/admin/collection/backfills/retryable/recover-holes", new { });
            var duplicate = await duplicateResponse.Content.ReadFromJsonAsync<BackfillHoleRecoveryResult>();
            Assert.AreEqual(0, duplicate?.TasksCreated, "An active task must not be duplicated.");

            var recovery = (await store.GetTasksAsync()).Single(x => x.TaskId != original.TaskId);
            Assert.IsTrue(await store.ReconcileDeadLetterAsync(recovery.TaskId, 1, now.AddSeconds(2), "again"));
            using var retryResponse = await client.PostAsJsonAsync(
                "/api/admin/collection/backfills/retryable/recover-holes", new { });
            var retry = await retryResponse.Content.ReadFromJsonAsync<BackfillHoleRecoveryResult>();
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
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", ResourceType.Race,
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
                "/api/admin/collection/backfills/complete/recover-holes", new { });
            var result = await response.Content.ReadFromJsonAsync<BackfillHoleRecoveryResult>();

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
            await store.RegisterDefinitionAsync(new("race-discovery"), "Race discovery", ResourceType.Race,
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
                "/api/admin/collection/backfills/large/recover-holes", new { });
            var result = await response.Content.ReadFromJsonAsync<BackfillHoleRecoveryResult>();

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
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse", ResourceType.Horse,
            1, "initial", false);
        return store;
    }

    private static async Task<WebApplication> CreateApplicationAsync(CollectionPlatformStore store)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(store);
        var app = builder.Build();
        app.MapCollectionPlatformEndpoints();
        await app.StartAsync();
        return app;
    }
}
