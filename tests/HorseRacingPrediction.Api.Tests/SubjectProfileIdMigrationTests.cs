using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Infrastructure.Persistence;
using EventFlow.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class SubjectProfileIdMigrationTests
{
    [TestMethod]
    public async Task PreviewAndApply_MigratesUniqueRaceReferencedHorse_AndReplayIsIdempotent()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse, 3, "test", false);
        var name = $"移行対象馬{Guid.NewGuid():N}";
        var raceId = await CreateRaceAsync(http, name);
        var targetId = DeterministicIdGenerator.BuildHorseId(name, null);
        var sourceId = $"horse-{Guid.NewGuid():D}";
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var source = await store.RequestAsync(new(ResourceType.Horse, "JRA", sourceId), definition, 3,
            CollectionReason.Discovery, now, CollectionLane.Realtime, (int)CollectionPriority.High,
            attributes: new Dictionary<string, string>
            {
                ["name"] = name,
                ["requestedByRaceId"] = raceId,
            });
        var lease = await store.AcquireAsync(source.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(source.TaskId, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "SubjectResourceMissing", "missing",
                FailureImpact: CollectionFailureImpact.Isolated)));

        using var previewResponse = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks", new { execute = false });
        var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var candidate = preview.RootElement.GetProperty("candidates").EnumerateArray().Single();
        Assert.AreEqual("AutoMigrate", candidate.GetProperty("classification").GetString());
        Assert.AreEqual(targetId, candidate.GetProperty("targetId").GetString());
        Assert.IsTrue(candidate.GetProperty("safeToExecute").GetBoolean());

        using var applyResponse = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks",
            new { execute = true, taskIds = new[] { source.TaskId } });
        var apply = JsonDocument.Parse(await applyResponse.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.OK, applyResponse.StatusCode);
        Assert.AreEqual(1, apply.RootElement.GetProperty("createdRecoveryTasks").GetInt32());
        var tasks = await store.GetTasksAsync(limit: 1000);
        Assert.AreEqual(CollectionTaskStatus.Cancelled, tasks.Single(item => item.TaskId == source.TaskId).Status);
        Assert.AreEqual(CollectionTaskStatus.Ready,
            tasks.Single(item => item.Resource.Id == targetId && item.Definition == definition).Status);
        Assert.HasCount(1, await store.GetAttemptsAsync(source.TaskId));

        var secondName = $"混在再実行馬{Guid.NewGuid():N}";
        var secondRaceId = await CreateRaceAsync(http, secondName);
        var second = await store.RequestAsync(new(ResourceType.Horse, "JRA", $"legacy-{Guid.NewGuid():N}"),
            definition, 3, CollectionReason.Discovery, now, attributes: new Dictionary<string, string>
            {
                ["name"] = secondName,
                ["requestedByRaceId"] = secondRaceId,
            });
        var secondLease = await store.AcquireAsync(second.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(second.TaskId, secondLease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "SubjectResourceMissing", "missing",
                FailureImpact: CollectionFailureImpact.Isolated)));

        using var replayResponse = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks",
            new { execute = true, taskIds = new[] { source.TaskId, second.TaskId } });
        var replay = JsonDocument.Parse(await replayResponse.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.AreEqual(2, replay.RootElement.GetProperty("selectedCount").GetInt32());
        Assert.AreEqual(1, replay.RootElement.GetProperty("createdRecoveryTasks").GetInt32());
        Assert.AreEqual(1, replay.RootElement.GetProperty("reusedRecoveryTasks").GetInt32());
        Assert.HasCount(4, await store.GetTasksAsync(limit: 1000));
    }

    [TestMethod]
    public async Task Preview_SameAuthoritativeIdIsRetryExisting_AndCannotExecute()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse, 3, "test", false);
        var name = $"既存対象馬{Guid.NewGuid():N}";
        var raceId = await CreateRaceAsync(http, name);
        var id = DeterministicIdGenerator.BuildHorseId(name, null);
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var source = await store.RequestAsync(new(ResourceType.Horse, "JRA", id), definition, 3,
            CollectionReason.Discovery, now, attributes: new Dictionary<string, string>
            {
                ["name"] = name,
                ["requestedByRaceId"] = raceId,
            });
        var lease = await store.AcquireAsync(source.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(source.TaskId, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "SubjectResourceMissing", "missing",
                FailureImpact: CollectionFailureImpact.Isolated)));

        using var previewResponse = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks", new { execute = false });
        var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var candidate = preview.RootElement.GetProperty("candidates").EnumerateArray().Single();
        Assert.AreEqual("RetryExisting", candidate.GetProperty("classification").GetString());
        Assert.IsFalse(candidate.GetProperty("safeToExecute").GetBoolean());

        using var apply = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks",
            new { execute = true, taskIds = new[] { source.TaskId } });
        Assert.AreEqual(HttpStatusCode.Conflict, apply.StatusCode);
        Assert.AreEqual(CollectionTaskStatus.Failed,
            (await store.GetTasksAsync(limit: 1000)).Single(item => item.TaskId == source.TaskId).Status);
    }

    [TestMethod]
    public async Task Apply_RequiresExplicitSelection()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

        using var response = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks", new { execute = true });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PreviewAndApply_MigratesJockeyAndTrainerFromRaceReferences()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("jockey-profile"), "Jockey profile", ResourceType.Jockey, 3, "test", false);
        await store.RegisterDefinitionAsync(new("trainer-profile"), "Trainer profile", ResourceType.Trainer, 3, "test", false);
        var horseName = $"移行根拠馬{Guid.NewGuid():N}";
        var jockeyName = $"移行 騎手{Guid.NewGuid():N}";
        var trainerName = $"移行 調教師{Guid.NewGuid():N}";
        var raceId = await CreateRaceAsync(http, horseName, jockeyName, trainerName);
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var sources = new List<Guid>();
        foreach (var item in new[]
                 {
                     (ResourceType.Jockey, Definition: "jockey-profile", Name: jockeyName),
                     (ResourceType.Trainer, Definition: "trainer-profile", Name: trainerName),
                 })
        {
            var receipt = await store.RequestAsync(new(item.Item1, "JRA", $"legacy-{Guid.NewGuid():N}"),
                new(item.Definition), 3, CollectionReason.Discovery, now,
                attributes: new Dictionary<string, string> { ["name"] = item.Name, ["requestedByRaceId"] = raceId });
            var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
            Assert.IsNotNull(lease);
            Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(1),
                new(CollectionAttemptResult.PermanentFailure, "SubjectResourceMissing", "missing",
                    FailureImpact: CollectionFailureImpact.Isolated)));
            sources.Add(receipt.TaskId);
        }

        using var previewResponse = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks", new { execute = false });
        var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var candidates = preview.RootElement.GetProperty("candidates").EnumerateArray().ToArray();
        Assert.HasCount(2, candidates);
        Assert.IsTrue(candidates.All(item => item.GetProperty("classification").GetString() == "AutoMigrate"));

        using var apply = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks",
            new { execute = true, taskIds = sources });
        var result = JsonDocument.Parse(await apply.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.OK, apply.StatusCode);
        Assert.AreEqual(2, result.RootElement.GetProperty("createdRecoveryTasks").GetInt32());
    }

    [TestMethod]
    public async Task Preview_HorseIdentityConflictIsBlocked()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse, 3, "test", false);
        var name = $"識別矛盾馬{Guid.NewGuid():N}";
        var raceId = await CreateRaceAsync(http, name);
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receipt = await store.RequestAsync(new(ResourceType.Horse, "JRA", $"legacy-{Guid.NewGuid():N}"),
            definition, 3, CollectionReason.Discovery, now, attributes: new Dictionary<string, string>
            {
                ["name"] = name,
                ["requestedByRaceId"] = raceId,
                ["sourceIdentity"] = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002026999999/00",
            });
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(lease);
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "SubjectResourceMissing", "missing",
                FailureImpact: CollectionFailureImpact.Isolated)));

        using var response = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks", new { execute = false });
        var preview = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var candidate = preview.RootElement.GetProperty("candidates").EnumerateArray().Single();
        Assert.AreEqual("IdentityConflict", candidate.GetProperty("classification").GetString());
        Assert.IsFalse(candidate.GetProperty("safeToExecute").GetBoolean());
    }

    [TestMethod]
    public async Task Apply_AlreadyCurrent_IsReplayableWithoutCreatingRecoveryTask()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse, 3, "test", false);
        var name = $"収集済移行馬{Guid.NewGuid():N}";
        var raceId = await CreateRaceAsync(http, name);
        var targetId = DeterministicIdGenerator.BuildHorseId(name, null);
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var target = await store.RequestAsync(new(ResourceType.Horse, "JRA", targetId), definition, 3,
            CollectionReason.Initial, now);
        var targetLease = await store.AcquireAsync(target.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(target.TaskId, targetLease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.Succeeded)));
        var source = await store.RequestAsync(new(ResourceType.Horse, "JRA", $"legacy-{Guid.NewGuid():N}"),
            definition, 3, CollectionReason.Discovery, now.AddMinutes(1), attributes:
            new Dictionary<string, string> { ["name"] = name, ["requestedByRaceId"] = raceId });
        var sourceLease = await store.AcquireAsync(source.TaskId, 1, now.AddMinutes(1), TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(source.TaskId, sourceLease!.LeaseToken, now.AddMinutes(1).AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "SubjectResourceMissing", "missing",
                FailureImpact: CollectionFailureImpact.Isolated)));

        using var previewResponse = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks", new { execute = false });
        var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        Assert.AreEqual("AlreadyCurrent", preview.RootElement.GetProperty("candidates").EnumerateArray()
            .Single().GetProperty("classification").GetString());

        foreach (var iteration in Enumerable.Range(0, 2))
        {
            using var response = await http.PostAsJsonAsync(
                "/api/admin/collection/maintenance/obsolete-subject-profile-tasks",
                new { execute = true, taskIds = new[] { source.TaskId } });
            var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"iteration {iteration}");
            Assert.AreEqual(1, result.RootElement.GetProperty("selectedCount").GetInt32());
            Assert.AreEqual(0, result.RootElement.GetProperty("createdRecoveryTasks").GetInt32());
            Assert.AreEqual(0, result.RootElement.GetProperty("reusedRecoveryTasks").GetInt32());
        }
        Assert.HasCount(2, await store.GetTasksAsync(limit: 1000));
    }

    [TestMethod]
    public async Task Apply_ResumesPendingMigrationMarkerAfterSourceWasRetired()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse, 3, "test", false);
        var name = $"再開対象馬{Guid.NewGuid():N}";
        var raceId = await CreateRaceAsync(http, name);
        var targetId = DeterministicIdGenerator.BuildHorseId(name, null);
        var sourceResource = new ResourceKey(ResourceType.Horse, "JRA", $"legacy-{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var source = await store.RequestAsync(sourceResource, definition, 3, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string> { ["name"] = name, ["requestedByRaceId"] = raceId });
        var lease = await store.AcquireAsync(source.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(source.TaskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "SubjectResourceMissing", "missing",
                FailureImpact: CollectionFailureImpact.Isolated)));
        await store.MarkSubjectProfileMigrationAsync(source.TaskId, targetId, true, true);
        await store.RetireObsoleteSubjectProfileTasksAsync([source.TaskId], DateTimeOffset.UtcNow);

        using var response = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks",
            new { execute = true, taskIds = new[] { source.TaskId } });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var state = await store.GetSubjectProfileMigrationStateAsync(source.TaskId);
        Assert.IsTrue(state!.Completed);
        await Assert.ThrowsExactlyAsync<CollectionResourceSuppressedException>(() => store.RequestAsync(
            sourceResource, definition, 3, CollectionReason.ManualRefresh, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public async Task Apply_DoesNotSuppressUnreferencedSourceWhenItsProjectionStillExists()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse, 3, "test", false);
        var name = $"旧投影保持馬{Guid.NewGuid():N}";
        var raceId = await CreateRaceAsync(http, name);
        var sourceId = $"horse-{Guid.NewGuid():D}";
        (await http.PostAsJsonAsync("/api/horses",
            new RegisterHorseRequest(name, name, null, null, HorseId: sourceId))).EnsureSuccessStatusCode();
        var resource = new ResourceKey(ResourceType.Horse, "JRA", sourceId);
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var source = await store.RequestAsync(resource, definition, 3, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string> { ["name"] = name, ["requestedByRaceId"] = raceId });
        var lease = await store.AcquireAsync(source.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(source.TaskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "SubjectResourceMissing", "missing",
                FailureImpact: CollectionFailureImpact.Isolated)));

        using var apply = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks",
            new { execute = true, taskIds = new[] { source.TaskId } });
        Assert.AreEqual(HttpStatusCode.OK, apply.StatusCode);
        var retry = await store.RequestAsync(resource, definition, 3, CollectionReason.ManualRefresh,
            DateTimeOffset.UtcNow);
        Assert.IsTrue(retry.CreatedTask);
    }

    [TestMethod]
    public async Task Preview_DistinguishesRepairProjectionAmbiguousAndUnverifiable()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse, 3, "test", false);

        var repairName = $"投影欠落馬{Guid.NewGuid():N}";
        var repairRaceId = await CreateRaceAsync(http, repairName);
        var repairTargetId = DeterministicIdGenerator.BuildHorseId(repairName, null);
        using (var db = application.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext())
        {
            var projection = await db.Set<HorseRacingPrediction.Application.Queries.ReadModels.HorseReadModel>()
                .SingleAsync(item => item.HorseId == repairTargetId);
            db.Remove(projection);
            await db.SaveChangesAsync();
        }
        await CreateFailedSourceAsync(store, definition, repairName, repairRaceId);

        var ambiguousName = $"同名候補馬{Guid.NewGuid():N}";
        var identity1 = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002026111111/00";
        var identity2 = "https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002026222222/00";
        var ambiguousRequest = new DeclareRaceResultBulkRequest(new DateOnly(2026, 9, 20),
            $"AMBIGUOUS-{Guid.NewGuid():N}", 2, "同名主体検証", EntryCount: 2,
            WinningHorseName: ambiguousName, DeclaredAt: DateTimeOffset.UtcNow,
            Entries:
            [
                new(1, 1, null, null, null, null, null, HorseName: ambiguousName, HorseSourceIdentity: identity1),
                new(2, 2, null, null, null, null, null, HorseName: ambiguousName, HorseSourceIdentity: identity2),
            ]);
        using var ambiguousResponse = await http.PostAsJsonAsync("/api/races/result-bulk", ambiguousRequest);
        var ambiguousBody = await ambiguousResponse.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.AreEqual(HttpStatusCode.OK, ambiguousResponse.StatusCode);
        Assert.IsNotNull(ambiguousBody);
        Assert.IsEmpty(ambiguousBody.Errors);
        await CreateFailedSourceAsync(store, definition, ambiguousName, ambiguousBody.RaceId);

        await CreateFailedSourceAsync(store, definition, $"根拠なし馬{Guid.NewGuid():N}",
            $"missing-race-{Guid.NewGuid():N}");

        using var response = await http.PostAsJsonAsync(
            "/api/admin/collection/maintenance/obsolete-subject-profile-tasks", new { execute = false });
        var preview = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var classifications = preview.RootElement.GetProperty("candidates").EnumerateArray()
            .Select(item => item.GetProperty("classification").GetString()).ToArray();
        CollectionAssert.AreEquivalent(new[] { "RepairProjection", "Ambiguous", "Unverifiable" }, classifications);
    }

    private static async Task CreateFailedSourceAsync(CollectionPlatformStore store,
        CollectionDefinitionId definition, string name, string raceId)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receipt = await store.RequestAsync(new(ResourceType.Horse, "JRA", $"legacy-{Guid.NewGuid():N}"),
            definition, 3, CollectionReason.Discovery, now,
            attributes: new Dictionary<string, string> { ["name"] = name, ["requestedByRaceId"] = raceId });
        var lease = await store.AcquireAsync(receipt.TaskId, 1, now, TimeSpan.FromMinutes(5));
        Assert.IsTrue(await store.CompleteAttemptAsync(receipt.TaskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.PermanentFailure, "SubjectResourceMissing", "missing",
                FailureImpact: CollectionFailureImpact.Isolated)));
    }

    private static async Task<string> CreateRaceAsync(HttpClient http, string horseName,
        string? jockeyName = null, string? trainerName = null)
    {
        var request = new DeclareRaceResultBulkRequest(new DateOnly(2026, 9, 20),
            $"MIGRATION-{Guid.NewGuid():N}", 1, "主体移行検証", EntryCount: 1,
            WinningHorseName: horseName, DeclaredAt: DateTimeOffset.UtcNow,
            Entries: [new RaceResultEntryBulkDto(1, 1, null, null, null, null, null,
                HorseName: horseName, JockeyName: jockeyName, TrainerName: trainerName)]);
        using var response = await http.PostAsJsonAsync("/api/races/result-bulk", request);
        var body = await response.Content.ReadFromJsonAsync<DeclareRaceResultBulkResponse>();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(body);
        Assert.IsEmpty(body.Errors, string.Join(" | ", body.Errors));
        return body.RaceId;
    }
}
