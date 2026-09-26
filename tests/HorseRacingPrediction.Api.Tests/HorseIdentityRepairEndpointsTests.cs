using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;
using EventFlow.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityRepairEndpointsTests
{
    [TestMethod]
    public async Task PreDispatchIssue_IsVisibleAndCreatesCanonicalRecoveryWithoutSourceSuppression()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var name = $"事前補正馬{Guid.NewGuid():N}";
        var targetId = DeterministicIdGenerator.BuildHorseId(name, null);
        var raceId = $"race-{Guid.NewGuid():D}";
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/horses",
            new RegisterHorseRequest(name, name, null, null, HorseId: targetId))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/races",
            new CreateRaceRequest(new DateOnly(2026, 9, 20), "TOKYO", 1, "事前補正", raceId))).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await http.PostAsJsonAsync($"/api/races/{raceId}/card/publish",
            new { EntryCount = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync($"/api/races/{raceId}/entries",
            new RegisterEntryRequest(targetId, 1, null, null, 1, 55, "M", 3, null, null,
                HorseName: name))).StatusCode);
        var issueId = Guid.NewGuid();
        using (var db = application.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext())
        {
            db.SubjectIdentificationRepairIssues.Add(new SubjectIdentificationRepairIssue
            {
                IssueId = issueId,
                SubjectType = "Horse",
                SubjectId = $"legacy-{Guid.NewGuid():N}",
                SubjectName = name,
                DefinitionId = "horse-profile",
                RequestedByRaceId = raceId,
                ReasonCode = "SubjectIdentityRepairRequired",
                ReasonMessage = "ID不整合",
                EvidenceFingerprint = Guid.NewGuid().ToString("N"),
                Status = "Open",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", ResourceType.Horse, 3, "test", false);

        var preview = await http.GetFromJsonAsync<SubjectIdentificationRepairPreviewResponse>(
            "/api/admin/repairs/subject-identification");
        var candidate = preview!.Candidates.Single(x => x.NotificationId == issueId);
        Assert.IsTrue(candidate.SafeToExecute);
        Assert.AreEqual(targetId, candidate.MergeTargetId);
        using var execute = await http.PostAsJsonAsync("/api/admin/repairs/subject-identification/execute",
            new ExecuteSubjectIdentificationRepairRequest([new(issueId)]));
        Assert.AreEqual(HttpStatusCode.Accepted, execute.StatusCode);
        Assert.IsTrue((await store.GetTasksAsync(limit: 100)).Any(x => x.Resource.Id == targetId));
        using var verifyDb = application.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext();
        var issue = (await verifyDb.SubjectIdentificationRepairIssues.FindAsync(issueId))!;
        Assert.AreEqual("Resolved", issue.Status);
        var recoveryTaskId = issue.RecoveryTaskId;
        issue.Status = "Open";
        issue.ResolvedAt = null;
        issue.RecoveryTaskId = null;
        await verifyDb.SaveChangesAsync();

        using var replay = await http.PostAsJsonAsync("/api/admin/repairs/subject-identification/execute",
            new ExecuteSubjectIdentificationRepairRequest([new(issueId)]));
        Assert.AreEqual(HttpStatusCode.Accepted, replay.StatusCode);
        var tasks = (await store.GetTasksAsync(limit: 100)).Where(x => x.Resource.Id == targetId).ToArray();
        Assert.HasCount(1, tasks);
        Assert.AreEqual(recoveryTaskId, tasks[0].TaskId);
    }

    [TestMethod]
    public async Task PreDispatchIssue_BatchValidationFailureCreatesNoRecoveryTask()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var name = $"一括検証馬{Guid.NewGuid():N}";
        var targetId = DeterministicIdGenerator.BuildHorseId(name, null);
        var raceId = $"race-{Guid.NewGuid():D}";
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/horses",
            new RegisterHorseRequest(name, name, null, null, HorseId: targetId))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/races",
            new CreateRaceRequest(new DateOnly(2026, 9, 20), "TOKYO", 2, "一括検証", raceId))).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await http.PostAsJsonAsync($"/api/races/{raceId}/card/publish",
            new { EntryCount = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync($"/api/races/{raceId}/entries",
            new RegisterEntryRequest(targetId, 1, null, null, 1, 55, "M", 3, null, null,
                HorseName: name))).StatusCode);
        var validIssueId = Guid.NewGuid();
        var invalidIssueId = Guid.NewGuid();
        using (var db = application.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext())
        {
            db.SubjectIdentificationRepairIssues.AddRange(
                CreateIssue(validIssueId, raceId, targetId, name),
                CreateIssue(invalidIssueId, $"race-{Guid.NewGuid():D}", $"legacy-{Guid.NewGuid():N}", name));
            await db.SaveChangesAsync();
        }
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", ResourceType.Horse, 3, "test", false);

        using var response = await http.PostAsJsonAsync("/api/admin/repairs/subject-identification/execute",
            new ExecuteSubjectIdentificationRepairRequest([new(validIssueId), new(invalidIssueId)]));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.IsFalse((await store.GetTasksAsync(limit: 100)).Any(x => x.Resource.Id == targetId));
    }

    [TestMethod]
    public async Task PreDispatchIssue_ChangedTargetWithinOccurrenceDoesNotReuseStaleRecovery()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var firstName = $"変更前馬{Guid.NewGuid():N}";
        var secondName = $"変更後馬{Guid.NewGuid():N}";
        var firstId = DeterministicIdGenerator.BuildHorseId(firstName, null);
        var secondId = DeterministicIdGenerator.BuildHorseId(secondName, null);
        var raceId = $"race-{Guid.NewGuid():D}";
        foreach (var (id, name) in new[] { (firstId, firstName), (secondId, secondName) })
            Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/horses",
                new RegisterHorseRequest(name, name, null, null, HorseId: id))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/races",
            new CreateRaceRequest(new DateOnly(2026, 9, 20), "TOKYO", 3, "変更検証", raceId))).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await http.PostAsJsonAsync($"/api/races/{raceId}/card/publish",
            new { EntryCount = 2 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync($"/api/races/{raceId}/entries",
            new RegisterEntryRequest(firstId, 1, null, null, 1, 55, "M", 3, null, null,
                HorseName: firstName))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync($"/api/races/{raceId}/entries",
            new RegisterEntryRequest(secondId, 2, null, null, 2, 55, "M", 3, null, null,
                HorseName: secondName))).StatusCode);
        var issueId = Guid.NewGuid();
        using (var db = application.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext())
        {
            db.SubjectIdentificationRepairIssues.Add(CreateIssue(issueId, raceId, $"legacy-{Guid.NewGuid():N}", firstName));
            await db.SaveChangesAsync();
        }
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", ResourceType.Horse, 3, "test", false);
        using var first = await http.PostAsJsonAsync("/api/admin/repairs/subject-identification/execute",
            new ExecuteSubjectIdentificationRepairRequest([new(issueId)]));
        Assert.AreEqual(HttpStatusCode.Accepted, first.StatusCode);
        using (var db = application.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext())
        {
            var issue = await db.SubjectIdentificationRepairIssues.FindAsync(issueId);
            issue!.Status = "Open";
            issue.ResolvedAt = null;
            issue.RecoveryTaskId = null;
            issue.TargetSubjectId = null;
            issue.SubjectName = secondName;
            await db.SaveChangesAsync();
        }

        using var changed = await http.PostAsJsonAsync("/api/admin/repairs/subject-identification/execute",
            new ExecuteSubjectIdentificationRepairRequest([new(issueId)]));

        Assert.AreEqual(HttpStatusCode.Conflict, changed.StatusCode);
        var recoveryIds = (await store.GetTasksAsync(limit: 100)).Select(x => x.Resource.Id).ToArray();
        CollectionAssert.Contains(recoveryIds, firstId);
        CollectionAssert.DoesNotContain(recoveryIds, secondId);
    }

    private static SubjectIdentificationRepairIssue CreateIssue(
        Guid issueId, string raceId, string subjectId, string name) => new()
        {
            IssueId = issueId,
            SubjectType = "Horse",
            SubjectId = subjectId,
            SubjectName = name,
            DefinitionId = "horse-profile",
            RequestedByRaceId = raceId,
            ReasonCode = "SubjectIdentityRepairRequired",
            ReasonMessage = "ID不整合",
            EvidenceFingerprint = Guid.NewGuid().ToString("N"),
            Status = "Open",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [TestMethod]
    public async Task SubjectNotIdentified_MissingName_IsBlockedBeforeRecovery()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("owner-identity"), "Owner identity", ResourceType.Owner,
            1, "test", false);
        var resource = new ResourceKey(ResourceType.Owner, "JRA", $"missing-{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receipt = await store.RequestAsync(resource, new("owner-identity"), 1,
            CollectionReason.Discovery, now);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(taskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "主体名がありません。",
                PageIdentification: "SubjectIdentification:MissingName"));

        var preview = await http.GetFromJsonAsync<SubjectIdentificationRepairPreviewResponse>(
            "/api/admin/repairs/subject-identification");
        var candidate = preview!.Candidates.Single(x => x.SubjectId == resource.Id);
        Assert.AreEqual("Blocked", candidate.Evaluation);
        Assert.IsFalse(candidate.SafeToExecute);
        StringAssert.Contains(candidate.BlockingReason, "主体名");

        using var response = await http.PostAsJsonAsync("/api/admin/repairs/subject-identification/execute",
            new ExecuteSubjectIdentificationRepairRequest(
                [new ExecuteSubjectIdentificationRepairItem(candidate.NotificationId)]));
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task SubjectNotIdentified_CanBeDismissedWithoutDeletingHistory()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("trainer-profile");
        await store.RegisterDefinitionAsync(definition, "Trainer profile", ResourceType.Trainer,
            1, "test", false);
        var resource = new ResourceKey(ResourceType.Trainer, "JRA", $"old-{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receipt = await store.RequestAsync(resource, definition, 1, CollectionReason.Discovery, now);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(taskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "主体名がありません。",
                PageIdentification: "SubjectIdentification:MissingName"));
        var candidate = (await http.GetFromJsonAsync<SubjectIdentificationRepairPreviewResponse>(
            "/api/admin/repairs/subject-identification"))!.Candidates.Single(x => x.SubjectId == resource.Id);

        using var response = await http.PostAsJsonAsync("/api/admin/repairs/subject-identification/dismiss",
            new DismissSubjectIdentificationFailuresRequest([candidate.NotificationId]));
        var result = await response.Content.ReadFromJsonAsync<DismissSubjectIdentificationFailuresResponse>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, result!.DismissedCount);
        Assert.IsEmpty((await http.GetFromJsonAsync<SubjectIdentificationRepairPreviewResponse>(
            "/api/admin/repairs/subject-identification"))!.Candidates.Where(x => x.SubjectId == resource.Id));
        Assert.AreEqual(CollectionTaskStatus.Failed,
            (await store.GetTasksAsync()).Single(x => x.TaskId == taskId).Status);
        var detail = await store.GetResourceDetailAsync(resource, definition);
        Assert.AreEqual(CollectionFailureResolutionStatus.Superseded,
            detail!.Failures!.Single().ResolutionStatus);

        using var repeatedResponse = await http.PostAsJsonAsync(
            "/api/admin/repairs/subject-identification/dismiss",
            new DismissSubjectIdentificationFailuresRequest([candidate.NotificationId]));
        var repeated = await repeatedResponse.Content
            .ReadFromJsonAsync<DismissSubjectIdentificationFailuresResponse>();
        Assert.AreEqual(HttpStatusCode.OK, repeatedResponse.StatusCode);
        Assert.AreEqual(0, repeated!.DismissedCount);
        Assert.AreEqual(1, repeated.AlreadyClosedCount);
    }

    [TestMethod]
    public async Task ObsoletePedigreeReference_IsRecommendedForDismissalInsteadOfRetry()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var definition = new CollectionDefinitionId("horse-profile");
        await store.RegisterDefinitionAsync(definition, "Horse profile", ResourceType.Horse, 3, "test", false);
        var resource = new ResourceKey(ResourceType.Horse, "JRA", $"obsolete-{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receipt = await store.RequestAsync(resource, definition, 3, CollectionReason.Discovery, now);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(taskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified",
                "同定不能: 公開検索に一致候補がありません。期待=Horse:トイムム 産駒"));

        var preview = await http.GetFromJsonAsync<SubjectIdentificationRepairPreviewResponse>(
            "/api/admin/repairs/subject-identification");
        var candidate = preview!.Candidates.Single(x => x.SubjectId == resource.Id);

        Assert.AreEqual("DismissRecommended", candidate.Evaluation);
        Assert.IsFalse(candidate.SafeToExecute);
        StringAssert.Contains(candidate.BlockingReason, "同じ条件では再失敗");
    }

    [TestMethod]
    public async Task DismissSubjectIdentification_WithNonSubjectNotification_UpdatesNothing()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var subjectDefinition = new CollectionDefinitionId("jockey-profile");
        var raceDefinition = new CollectionDefinitionId("race-test");
        await store.RegisterDefinitionAsync(subjectDefinition, "Jockey profile", ResourceType.Jockey,
            1, "test", false);
        await store.RegisterDefinitionAsync(raceDefinition, "Race", ResourceType.Race,
            1, "test", false);
        foreach (var item in new[]
                 {
                     (new ResourceKey(ResourceType.Jockey, "JRA", "jockey-old"), subjectDefinition),
                     (new ResourceKey(ResourceType.Race, "JRA", "race-old"), raceDefinition),
                 })
        {
            var receipt = await store.RequestAsync(item.Item1, item.Item2, 1, CollectionReason.Discovery, now);
            var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
            var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
            await store.CompleteAttemptAsync(taskId, lease!.LeaseToken, now.AddSeconds(1),
                new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "not found",
                    FailureImpact: CollectionFailureImpact.Isolated));
        }
        var notifications = await store.GetActionableFailureNotificationsAsync(now.AddMinutes(1), 10);

        using var response = await http.PostAsJsonAsync("/api/admin/repairs/subject-identification/dismiss",
            new DismissSubjectIdentificationFailuresRequest(notifications.Select(x => x.NotificationId).ToArray()));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.HasCount(2, await store.GetActionableFailureNotificationsAsync(now.AddMinutes(1), 10));
    }

    [TestMethod]
    public async Task SubjectNotIdentified_WithSafeHorseCandidate_MergesSuppressesAndRecoversTarget()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"統合再収集馬{suffix}";
        var sourceId = DeterministicIdGenerator.BuildHorseId(name);
        var sourceUrl = $"https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud00{suffix}/45";
        var targetId = DeterministicIdGenerator.BuildHorseId(name, sourceUrl);
        var raceId = $"race-{Guid.NewGuid()}";
        await http.PostAsJsonAsync("/api/horses", new RegisterHorseRequest(name, name, null, null, HorseId: sourceId));
        await http.PostAsJsonAsync("/api/horses", new RegisterHorseRequest(name, name, null, null, HorseId: targetId));
        await http.PostAsJsonAsync("/api/races",
            new CreateRaceRequest(new DateOnly(2026, 9, 14), "TOKYO", 2, "統合再収集", raceId));
        await http.PostAsJsonAsync($"/api/races/{raceId}/card/publish", new { EntryCount = 1 });
        var entryId = DeterministicIdGenerator.BuildRaceEntryId(raceId, targetId);
        (await http.PostAsJsonAsync($"/api/races/{raceId}/entries",
            new RegisterEntryRequest(targetId, 1, null, null, 1, 55, "M", 3, null, null,
                EntryId: entryId, HorseName: name, HorseSourceIdentity: sourceUrl))).EnsureSuccessStatusCode();
        // The identity repair endpoint consumes persisted evidence, not a new entry rebind.
        using (var repairDb = application.Services.GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext())
        {
            repairDb.HorseIdentityRepairCandidates.Add(new HorseIdentityRepairCandidateReadModel
            {
                CandidateId = $"20260913-jra-horse-identity-repair:{raceId}:{entryId}",
                RepairId = "20260913-jra-horse-identity-repair",
                SourceHorseId = sourceId,
                TargetHorseId = targetId,
                JraIdentity = $"pw01dud00{suffix}/45",
                RaceId = raceId,
                EntryId = entryId,
                DetectedAt = DateTimeOffset.UtcNow,
            });
            await repairDb.SaveChangesAsync();
        }

        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", ResourceType.Horse,
            1, "test", false);
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var failed = await store.RequestAsync(new(ResourceType.Horse, "JRA", sourceId),
            new("horse-profile"), 1, CollectionReason.Discovery, now, explicitUrl: new Uri(sourceUrl));
        var failedTaskId = failed.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var lease = await store.AcquireAsync(failedTaskId, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(failedTaskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "識別失敗",
                new Uri(sourceUrl), new Uri(sourceUrl)));
        var preview = await http.GetFromJsonAsync<SubjectIdentificationRepairPreviewResponse>(
            "/api/admin/repairs/subject-identification");
        var candidate = preview!.Candidates.Single(x => x.SubjectId == sourceId);
        Assert.AreEqual("MergeReady", candidate.Evaluation);
        Assert.AreEqual(targetId, candidate.MergeTargetId);

        using var response = await http.PostAsJsonAsync("/api/admin/repairs/subject-identification/execute",
            new ExecuteSubjectIdentificationRepairRequest(
                [new ExecuteSubjectIdentificationRepairItem(candidate.NotificationId)]));
        var result = await response.Content.ReadFromJsonAsync<ExecuteSubjectIdentificationRepairResponse>();
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        Assert.AreEqual(1, result!.MergedCount);
        Assert.IsTrue((await store.GetTasksAsync()).Any(x => x.Resource.Id == targetId
            && x.Status == CollectionTaskStatus.Ready));
        await Assert.ThrowsExactlyAsync<CollectionResourceSuppressedException>(() => store.RequestAsync(
            new(ResourceType.Horse, "JRA", sourceId), new("horse-profile"), 1,
            CollectionReason.ManualRefresh, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public async Task SubjectIdentificationPreview_IncludesExactFailureForAllFourSubjectTypesOnly()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var subjectTypes = new[] { ResourceType.Horse, ResourceType.Jockey, ResourceType.Trainer, ResourceType.Owner };
        foreach (var type in subjectTypes)
        {
            var definition = new CollectionDefinitionId($"{type.ToString().ToLowerInvariant()}-identity");
            await store.RegisterDefinitionAsync(definition, type.ToString(), type, 1, "test", false);
            var receipt = await store.RequestAsync(new(type, "JRA", $"{type}-id"), definition, 1,
                CollectionReason.Discovery, now);
            var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
            var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
            var url = new Uri($"https://www.jra.go.jp/JRADB/accessU.html?CNAME={type}-identity");
            await store.CompleteAttemptAsync(taskId, lease!.LeaseToken, now.AddSeconds(1),
                new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "0件", url, url));
        }
        await store.RegisterDefinitionAsync(new("race-test"), "Race", ResourceType.Race, 1, "test", false);
        var excluded = await store.RequestAsync(new(ResourceType.Race, "JRA", "race-id"), new("race-test"), 1,
            CollectionReason.Discovery, now);
        var excludedTaskId = excluded.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var excludedLease = await store.AcquireAsync(excludedTaskId, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(excludedTaskId, excludedLease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "race"));

        var preview = await http.GetFromJsonAsync<SubjectIdentificationRepairPreviewResponse>(
            "/api/admin/repairs/subject-identification");

        CollectionAssert.AreEquivalent(subjectTypes, preview!.Candidates.Select(x => x.SubjectType).ToArray());
        Assert.IsTrue(preview.Candidates.All(x => x.SafeToExecute));
    }

    [TestMethod]
    public async Task SubjectNotIdentified_WithNoMergeCandidate_CanRecoverFromValidatedUrl()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", ResourceType.Horse,
            1, "test", false);
        var resource = new ResourceKey(ResourceType.Horse, "JRA", $"unresolved-{Guid.NewGuid():N}");
        var url = new Uri("https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud001234567890/01");
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receipt = await store.RequestAsync(resource, new("horse-profile"), 1,
            CollectionReason.Discovery, now, explicitUrl: url);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(taskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "0件でした", url, url));

        var preview = await http.GetFromJsonAsync<SubjectIdentificationRepairPreviewResponse>(
            "/api/admin/repairs/subject-identification");
        var candidate = preview!.Candidates.Single(x => x.SubjectId == resource.Id);
        Assert.AreEqual("RetryReady", candidate.Evaluation);
        Assert.IsTrue(candidate.SafeToExecute);
        Assert.IsNull(candidate.HorseMergeCandidateId, "候補0件は通常の再収集として扱う必要があります。");

        using var response = await http.PostAsJsonAsync("/api/admin/repairs/subject-identification/execute",
            new ExecuteSubjectIdentificationRepairRequest(
                [new ExecuteSubjectIdentificationRepairItem(candidate.NotificationId)]));
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ExecuteSubjectIdentificationRepairResponse>();
        Assert.AreEqual(1, result!.CreatedTaskCount);
        Assert.IsEmpty((await store.GetActionableFailureNotificationsAsync(DateTimeOffset.UtcNow, 10))
            .Where(x => x.NotificationId == candidate.NotificationId));
    }

    [TestMethod]
    public async Task SubjectNotIdentified_RegistrationMarkMismatch_UsesStoredUrlForRecovery()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("horse-profile"), "Horse profile", ResourceType.Horse,
            2, "test", false);
        var resource = new ResourceKey(ResourceType.Horse, "JRA", $"marked-{Guid.NewGuid():N}");
        var url = new Uri("https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud002011110091/B0");
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receipt = await store.RequestAsync(resource, new("horse-profile"), 2,
            CollectionReason.Discovery, now, explicitUrl: url);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
        const string error = "同定不能: 取得プロフィールの名前が一致しません。" +
            "期待=Horse::アジアエクスプレス; 取得名=マルガイ   アジアエクスプレス; " +
            "候補=アジアエクスプレス [/JRADB/accessU.html?CNAME=pw01dud002011110091/B0]";
        await store.CompleteAttemptAsync(taskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", error, null, url));

        var preview = await http.GetFromJsonAsync<SubjectIdentificationRepairPreviewResponse>(
            "/api/admin/repairs/subject-identification");
        var candidate = preview!.Candidates.Single(x => x.SubjectId == resource.Id);
        Assert.AreEqual("RetryReady", candidate.Evaluation);
        Assert.IsTrue(candidate.SafeToExecute);
        Assert.AreEqual(url.AbsoluteUri, candidate.SuggestedUrl);

        using var response = await http.PostAsJsonAsync("/api/admin/repairs/subject-identification/execute",
            new ExecuteSubjectIdentificationRepairRequest(
                [new ExecuteSubjectIdentificationRepairItem(candidate.NotificationId)]));
        var result = await response.Content.ReadFromJsonAsync<ExecuteSubjectIdentificationRepairResponse>();
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        Assert.AreEqual(1, result!.CreatedTaskCount);
        var detail = await store.GetResourceDetailPagedAsync(resource, new("horse-profile"));
        Assert.IsTrue(detail!.Requests.Any(x => x.Reason == CollectionReason.Recovery
            && x.ExplicitUrl == url.AbsoluteUri));
    }

    [TestMethod]
    [DataRow("マルガイ   アジアエクスプレス", true)]
    [DataRow("マル外　アジアエクスプレス", true)]
    [DataRow("マル地 アジアエクスプレス", true)]
    [DataRow("マルチ アジアエクスプレス", true)]
    [DataRow("マルガイ アジアエクスプレスII", false)]
    [DataRow("アジアエクスプレス", false)]
    public void RegistrationMarkMismatch_RequiresKnownPrefixAndExactNormalizedName(
        string actualName, bool expected)
    {
        var message = "同定不能: 取得プロフィールの名前が一致しません。" +
            $"期待=Horse::アジアエクスプレス; 取得名={actualName}; 候補=fixture";

        Assert.AreEqual(expected,
            EndpointExtensions.IsCorrectableHorseRegistrationMarkMismatch(message));
    }

    [TestMethod]
    public async Task SubjectNotIdentified_ParameterlessJraUrl_RemainsBlockedWithoutSafeCorrection()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = application.Services.GetRequiredService<CollectionPlatformStore>();
        await store.RegisterDefinitionAsync(new("trainer-profile"), "Trainer profile", ResourceType.Trainer,
            1, "test", false);
        var resource = new ResourceKey(ResourceType.Trainer, "JRA", $"unresolved-{Guid.NewGuid():N}");
        var invalid = new Uri("https://www.jra.go.jp/JRADB/accessD.html");
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var receipt = await store.RequestAsync(resource, new("trainer-profile"), 1,
            CollectionReason.Discovery, now, explicitUrl: invalid);
        var taskId = receipt.TaskId ?? throw new InvalidOperationException("No-hold request must produce a task id.");
        var lease = await store.AcquireAsync(taskId, 1, now, TimeSpan.FromMinutes(5));
        await store.CompleteAttemptAsync(taskId, lease!.LeaseToken, now.AddSeconds(1),
            new(CollectionAttemptResult.ResourceNotFound, "SubjectNotIdentified", "0件でした", invalid, invalid));
        var failure = (await store.GetActionableFailureNotificationsAsync(DateTimeOffset.UtcNow, 10)).Single();

        var preview = await http.GetFromJsonAsync<SubjectIdentificationRepairPreviewResponse>(
            "/api/admin/repairs/subject-identification");
        var candidate = preview!.Candidates.Single(x => x.NotificationId == failure.NotificationId);
        Assert.AreEqual("Blocked", candidate.Evaluation);
        Assert.IsFalse(candidate.SafeToExecute);
        Assert.IsNull(candidate.SuggestedUrl);

        using var response = await http.PostAsJsonAsync("/api/admin/repairs/subject-identification/execute",
            new ExecuteSubjectIdentificationRepairRequest(
                [new ExecuteSubjectIdentificationRepairItem(candidate.NotificationId, invalid.AbsoluteUri)]));
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task StoredHorseIdentityEvidence_CanBePreviewedAndAppliedIdempotently()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"修復テスト馬{suffix}";
        var sourceId = DeterministicIdGenerator.BuildHorseId(name);
        var sourceUrl = $"https://www.jra.go.jp/JRADB/accessU.html?CNAME=pw01dud00{suffix}/45";
        var targetId = DeterministicIdGenerator.BuildHorseId(name, sourceUrl);
        var raceId = $"race-{Guid.NewGuid()}";

        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/horses",
            new RegisterHorseRequest(name, name, null, null, HorseId: sourceId))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/horses",
            new RegisterHorseRequest(name, name, null, null, HorseId: targetId))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync("/api/races",
            new CreateRaceRequest(new DateOnly(2026, 9, 13), "TOKYO", 1, "修復テスト", raceId))).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await http.PostAsJsonAsync($"/api/races/{raceId}/card/publish",
            new { EntryCount = 1 })).StatusCode);
        var entryId = DeterministicIdGenerator.BuildRaceEntryId(raceId, targetId);
        Assert.AreEqual(HttpStatusCode.Created, (await http.PostAsJsonAsync($"/api/races/{raceId}/entries",
            new RegisterEntryRequest(targetId, 1, null, null, 1, 55, "M", 3, null, null,
                EntryId: entryId, HorseName: name, HorseSourceIdentity: sourceUrl))).StatusCode);
        using (var repairDb = application.Services
                   .GetRequiredService<IDbContextProvider<EventStoreDbContext>>().CreateContext())
        {
            repairDb.HorseIdentityRepairCandidates.Add(new HorseIdentityRepairCandidateReadModel
            {
                CandidateId = $"20260913-jra-horse-identity-repair:{raceId}:{entryId}",
                RepairId = "20260913-jra-horse-identity-repair",
                SourceHorseId = sourceId,
                TargetHorseId = targetId,
                JraIdentity = $"pw01dud00{suffix}/45",
                RaceId = raceId,
                EntryId = entryId,
                DetectedAt = DateTimeOffset.UtcNow,
            });
            repairDb.HorseIdentityRepairCandidates.Add(new HorseIdentityRepairCandidateReadModel
            {
                CandidateId = $"20260913-jra-horse-identity-repair:{raceId}:duplicate-evidence",
                RepairId = "20260913-jra-horse-identity-repair",
                SourceHorseId = sourceId,
                TargetHorseId = targetId,
                JraIdentity = $"pw01dud00{suffix}/45",
                RaceId = raceId,
                EntryId = entryId,
                DetectedAt = DateTimeOffset.UtcNow,
            });
            await repairDb.SaveChangesAsync();
        }
        var collectionStore = application.Services.GetRequiredService<CollectionPlatformStore>();
        var sourceResource = new ResourceKey(ResourceType.Horse, "JRA", sourceId);
        const int revision = 1;
        await collectionStore.RegisterDefinitionAsync(new("horse-profile"), "Horse profile",
            ResourceType.Horse, revision, "test", false);
        var sourceTask = await collectionStore.RequestAsync(sourceResource, new("horse-profile"), revision,
            CollectionReason.ManualRefresh, DateTimeOffset.UtcNow);

        var preview = await http.GetFromJsonAsync<HorseIdentityRepairPreviewResponse>(
            "/api/admin/repairs/20260913-jra-horse-identity");
        var candidates = preview!.Candidates.ToArray();
        Assert.HasCount(2, candidates);
        Assert.IsTrue(candidates.All(x => x.SafeToApply), string.Join("; ", candidates.Select(x => x.BlockingReason)));
        Assert.IsTrue(candidates.All(x => x.SourceHorseId == sourceId));
        Assert.IsTrue(candidates.All(x => x.TargetHorseId == targetId));

        var apply = await http.PostAsJsonAsync("/api/admin/repairs/20260913-jra-horse-identity/apply",
            new ApplyHorseIdentityRepairRequest(candidates.Select(x => x.CandidateId).ToArray()));
        Assert.AreEqual(HttpStatusCode.OK, apply.StatusCode);
        var applied = await apply.Content.ReadFromJsonAsync<ApplyHorseIdentityRepairResponse>();
        Assert.AreEqual(1, applied!.DisabledCollectionTaskCount);
        Assert.AreEqual(CollectionTaskStatus.Cancelled,
            (await collectionStore.GetTasksAsync()).Single(x => x.TaskId == sourceTask.TaskId).Status);
        await Assert.ThrowsExactlyAsync<CollectionResourceSuppressedException>(() =>
            collectionStore.RequestAsync(sourceResource, new("horse-profile"), revision,
                CollectionReason.ManualRefresh, DateTimeOffset.UtcNow));
        var oldProfile = await http.GetAsync($"/api/horses/{sourceId}");
        Assert.AreEqual(HttpStatusCode.OK, oldProfile.StatusCode);
        var resolved = await oldProfile.Content.ReadFromJsonAsync<HorseRacingPrediction.Contracts.HorseReadModel>();
        Assert.AreEqual(targetId, resolved!.HorseId);

        var second = await http.PostAsJsonAsync("/api/admin/repairs/20260913-jra-horse-identity/apply",
            new ApplyHorseIdentityRepairRequest(candidates.Select(x => x.CandidateId).ToArray()));
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        var repeated = await second.Content.ReadFromJsonAsync<ApplyHorseIdentityRepairResponse>();
        Assert.AreEqual(0, repeated!.AppliedCount);
        Assert.AreEqual(2, repeated.SkippedCount);
    }
}
