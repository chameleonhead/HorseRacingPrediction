using System.Net;
using System.Net.Http.Json;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class JobManagementEndpointTests
{
    [TestMethod]
    public async Task GetJobs_ReturnsApiOwnedJobs()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var requestClient = client;
        requestClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        await store.ScheduleJobAsync("Collection", "target", "{}", DateTimeOffset.UtcNow);

        var response = await requestClient.GetAsync("/api/admin/jobs");
        var jobs = await response.Content.ReadFromJsonAsync<AgentJobStatusReadModel[]>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(jobs!.Any(x => x.JobId == "Collection:target"));
    }

    [TestMethod]
    public async Task SearchJobs_PagesAcrossAllRowsAndReturnsCompleteDaySummary()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var requestClient = client;
        requestClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        var now = new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 105; index++)
            await store.ScheduleJobAsync("RaceCardCollectionRequest", $"2026-08-30-race-{index:D3}", "{}", now.AddMinutes(index));

        var response = await requestClient.GetFromJsonAsync<AgentJobSearchResult>(
            "/api/admin/jobs/search?view=all&query=%E5%87%BA%E8%B5%B0%E8%A1%A8&page=2&pageSize=50");

        Assert.IsNotNull(response);
        Assert.AreEqual(105, response.TotalCount);
        Assert.AreEqual(50, response.Items.Count);
        var day = response.DaySummaries.Single(x => x.Date == "2026-08-30");
        Assert.AreEqual(105, day.Count);
        Assert.AreEqual(105, day.WaitingCount);
    }

    [TestMethod]
    public async Task Reacquire_SucceededJob_CreatesDifferentJob()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var requestClient = client;
        requestClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        var now = DateTimeOffset.UtcNow;
        await store.ScheduleJobAsync("Collection", "source", "{}", now);
        await store.CompleteJobAsync("Collection", "source");
        var source = (await store.GetJobDetailAsync("Collection:source"))!;

        var response = await requestClient.PostAsJsonAsync($"/api/admin/jobs/{Uri.EscapeDataString(source.JobId)}/reacquire", new { expectedUpdatedAt = source.UpdatedAt, reason = "refresh" });
        var body = await response.Content.ReadFromJsonAsync<ReacquireResponse>();

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        Assert.AreNotEqual(source.JobId, body!.JobId);
        Assert.AreEqual(AgentJobStatus.Succeeded, (await store.GetJobDetailAsync(source.JobId))!.Status);
        Assert.AreEqual(AgentJobStatus.Ready, (await store.GetJobDetailAsync(body.JobId))!.Status);
    }

    [TestMethod]
    public async Task GetAcquisitionDetail_ReturnsOriginJobId()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var requestClient = client;
        requestClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        var now = new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);
        await store.ScheduleJobAsync("RaceCardCollection", "20260830-niigata-11", "{}", now);
        await store.UpsertAgentAcquisitionStatusAsync(
            "Horse:EntityUpsert:テストホース:20260830-niigata-11",
            AgentAcquisitionSubjectType.Horse,
            AgentAcquisitionOperationType.EntityUpsert,
            "JRA",
            "horse-test",
            "テストホース",
            "20260830-niigata-11",
            "RaceCardCollection:20260830-niigata-11",
            "https://example.test/race",
            RaceDataCollectionState.Succeeded,
            null,
            null,
            now);

        var response = await requestClient.GetAsync("/api/collection/acquisitions/Horse%3AEntityUpsert%3A%E3%83%86%E3%82%B9%E3%83%88%E3%83%9B%E3%83%BC%E3%82%B9%3A20260830-niigata-11");
        var body = await response.Content.ReadFromJsonAsync<AgentAcquisitionStatusReadModel>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("RaceCardCollection:20260830-niigata-11", body!.OriginJobId);
    }

    [TestMethod]
    public async Task GetAcquisitionHistory_ReturnsLatestAttemptsFirst()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var requestClient = client;
        requestClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        var key = "Horse:ProfileSync:履歴テスト";
        var now = new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);
        await store.UpsertAgentAcquisitionStatusAsync(key, AgentAcquisitionSubjectType.Horse, AgentAcquisitionOperationType.ProfileSync, "JRA", "horse-history", "履歴テスト", null, null, null, RaceDataCollectionState.Failed, RaceDataCollectionErrorCode.ExternalRequestFailed, "timeout", now);
        await store.UpsertAgentAcquisitionStatusAsync(key, AgentAcquisitionSubjectType.Horse, AgentAcquisitionOperationType.ProfileSync, "JRA", "horse-history", "履歴テスト", null, null, null, RaceDataCollectionState.Succeeded, null, null, now.AddMinutes(1));

        var response = await requestClient.GetAsync($"/api/collection/acquisitions/{Uri.EscapeDataString(key)}/history");
        var body = await response.Content.ReadFromJsonAsync<AgentAcquisitionHistoryReadModel[]>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(body);
        Assert.AreEqual(2, body.Length);
        Assert.AreEqual(RaceDataCollectionState.Succeeded, body[0].Status);
        Assert.AreEqual("timeout", body[1].ErrorReason);
    }

    [TestMethod]
    public async Task PauseAndResume_ReturnsRequeuedReadyDispatchCount()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var requestClient = client;
        requestClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        var now = DateTimeOffset.UtcNow;
        await store.ScheduleJobAsync("RaceCardCollection", "resume-source", "{}", now);
        var dispatch = (await store.GetPendingCollectionTaskDispatchesAsync(now.AddSeconds(1), 10)).Single();
        await store.MarkCollectionTaskDispatchedAsync(dispatch.OutboxId, now.AddSeconds(2));

        var pause = await requestClient.PostAsync("/api/admin/jobs/pause", null);
        var resume = await requestClient.PostAsync("/api/admin/jobs/resume", null);
        var body = await resume.Content.ReadFromJsonAsync<ResumeResponse>();

        Assert.AreEqual(HttpStatusCode.Accepted, pause.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, resume.StatusCode);
        Assert.AreEqual("Running", body!.Status);
        Assert.AreEqual(1, body.Requeued);
    }

    [TestMethod]
    public async Task QueueState_ReflectsPauseAndResume()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var requestClient = client;
        requestClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

        var initial = await requestClient.GetFromJsonAsync<QueueStateResponse>("/api/admin/jobs/queue-state");
        var pause = await requestClient.PostAsync("/api/admin/jobs/pause", null);
        var paused = await requestClient.GetFromJsonAsync<QueueStateResponse>("/api/admin/jobs/queue-state");
        var resume = await requestClient.PostAsync("/api/admin/jobs/resume", null);
        var resumed = await requestClient.GetFromJsonAsync<QueueStateResponse>("/api/admin/jobs/queue-state");

        Assert.IsFalse(initial!.IsPaused);
        Assert.AreEqual(HttpStatusCode.Accepted, pause.StatusCode);
        Assert.IsTrue(paused!.IsPaused);
        Assert.AreEqual(HttpStatusCode.OK, resume.StatusCode);
        Assert.IsFalse(resumed!.IsPaused);
    }

    [TestMethod]
    public async Task Pause_BlocksJobMutationsButAllowsResume()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var disposable = app;
        using var requestClient = client;
        requestClient.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        var now = DateTimeOffset.UtcNow;
        await store.ScheduleJobAsync("Collection", "source", "{}", now);
        await store.CompleteJobAsync("Collection", "source");
        var source = (await store.GetJobDetailAsync("Collection:source"))!;

        var pause = await requestClient.PostAsync("/api/admin/jobs/pause", null);
        var blocked = await requestClient.PostAsJsonAsync(
            $"/api/admin/jobs/{Uri.EscapeDataString(source.JobId)}/reacquire",
            new { expectedUpdatedAt = source.UpdatedAt, reason = "blocked while paused" });
        var resume = await requestClient.PostAsync("/api/admin/jobs/resume", null);

        Assert.AreEqual(HttpStatusCode.Accepted, pause.StatusCode);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, resume.StatusCode);
    }

    private sealed record ReacquireResponse(string JobId);
    private sealed record ResumeResponse(string Status, int Requeued);
    private sealed record QueueStateResponse(bool IsPaused);
}
