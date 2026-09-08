using System.Net;
using System.Net.Http.Json;
using Bunit;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Pages;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class JobHoldTests
{
    [TestMethod]
    public async Task HoldEndpoints_RequireAuthenticationAndRejectReleaseUntilAcknowledged()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        await store.ScheduleJobAsync("Collection", "one", "{}", DateTimeOffset.UtcNow);
        var task = (await store.AcquireCollectionTaskAsync("Collection", "one", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30)))!;
        var job = (await store.GetJobDetailAsync(task.TaskId))!;
        var path = $"/api/admin/jobs/{Uri.EscapeDataString(job.JobId)}";
        var denied = await http.PostAsJsonAsync(path + "/hold", new { expectedUpdatedAt = job.UpdatedAt });
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        Assert.AreEqual(HttpStatusCode.Accepted, (await http.PostAsJsonAsync(path + "/hold", new { expectedUpdatedAt = job.UpdatedAt })).StatusCode);
        job = (await store.GetJobDetailAsync(task.TaskId))!;
        Assert.AreEqual(HttpStatusCode.Conflict, (await http.PostAsJsonAsync(path + "/release-hold", new { expectedUpdatedAt = job.UpdatedAt })).StatusCode);
        await http.PostAsync("/api/admin/jobs/pause", null);
        var proxy = HttpProcessingStateStoreProxy.Create(http);
        Assert.AreEqual(CollectionLeaseControl.Hold, await proxy.GetCollectionLeaseControlAsync(task.TaskId, task.LeaseToken));
        Assert.IsTrue(await proxy.AcknowledgeCollectionHoldAsync(task.TaskId, task.LeaseToken));
        job = (await store.GetJobDetailAsync(task.TaskId))!;
        Assert.AreEqual(HttpStatusCode.Accepted, (await http.PostAsJsonAsync(path + "/release-hold", new { expectedUpdatedAt = job.UpdatedAt })).StatusCode);
        Assert.IsNull(await proxy.AcquireCollectionTaskAsync("Collection", "one", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30)));
    }

    [TestMethod]
    public async Task Detail_ShowsCancellationPendingThenReleaseAndAudit()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        await store.ScheduleJobAsync("Collection", "component", "{}", DateTimeOffset.UtcNow);
        var task = (await store.AcquireCollectionTaskAsync("Collection", "component", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30)))!;
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton(new AdminApiClient(http, new AdminApiBaseAddressResolver(app.Services.GetRequiredService<IServer>()),
            Options.Create(new ApiKeyOptions { Key = TestApplicationFactory.TestApiKey })));
        var cut = context.Render<JobDetail>(p => p.Add(x => x.JobId, task.TaskId));
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "保留して中断"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("保留して中断</")).Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "保留要求済み・中断待ち"));
        Assert.IsTrue(cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("保留を解除</")).Instance.Disabled);
        await store.AcknowledgeCollectionHoldAsync(task.TaskId, task.LeaseToken);
        cut.WaitForAssertion(() => Assert.IsFalse(cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("保留を解除</")).Instance.Disabled), TimeSpan.FromSeconds(6));
        await store.PauseCollectionAsync("テスト停止", null);
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("保留を解除</")).Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "収集全体の再開を待っています"));
        Assert.IsFalse((await store.GetJobDetailAsync(task.TaskId))!.IsHeld);
        Assert.HasCount(2, (await store.GetJobDetailAsync(task.TaskId))!.AuditHistory);
    }

    [TestMethod]
    public async Task Detail_LoadFailureShowsRetryAndRecoversWithoutOfferingHoldBeforeLoad()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var original = client;
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        await store.ScheduleJobAsync("Collection", "load-error", "{}", DateTimeOffset.UtcNow);
        var job = (await store.GetJobDetailAsync("Collection:load-error"))!;
        var handler = new LoadHandler(job);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton(new AdminApiClient(http, new AdminApiBaseAddressResolver(app.Services.GetRequiredService<IServer>()),
            Options.Create(new ApiKeyOptions { Key = TestApplicationFactory.TestApiKey })));
        var cut = context.Render<JobDetail>(p => p.Add(x => x.JobId, job.JobId));
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "収集ジョブを取得できませんでした"));
        Assert.IsFalse(cut.FindComponents<FluentButton>().Any(x => x.Markup.Contains(">保留<")));
        handler.Fail = false;
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("再試行</")).Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => Assert.IsTrue(cut.FindComponents<FluentButton>().Any(x => x.Markup.Contains(">保留<"))));
    }

    private sealed class LoadHandler(AgentJobDetailReadModel job) : HttpMessageHandler
    {
        public bool Fail { get; set; } = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(job) });
    }
}
