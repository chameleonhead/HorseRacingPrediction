using System.Net.Http.Json;
using Bunit;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Shared;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class SubjectCollectionComponentTests
{
    [TestMethod]
    public async Task HistoryRequest_ShowsProgressFailureAndRetryWithoutCreatingAnotherJob()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var id = "horse-" + Guid.NewGuid();
        (await http.PostAsJsonAsync("/api/horses", new RegisterHorseRequest("テスト馬", "テスト馬", null, null, id))).EnsureSuccessStatusCode();
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton(new AdminApiClient(http, new AdminApiBaseAddressResolver(app.Services.GetRequiredService<IServer>()),
            Options.Create(new ApiKeyOptions { Key = TestApplicationFactory.TestApiKey })));
        var completed = false;
        var cut = context.Render<SubjectCollectionAction>(p => p.Add(x => x.SubjectType, "Horse")
            .Add(x => x.SubjectId, id).Add(x => x.SubjectName, "テスト馬").Add(x => x.Operation, "history")
            .Add(x => x.OnCompleted, () => completed = true));
        var open = cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("過去レースを取得</"));
        cut.WaitForAssertion(() => Assert.IsFalse(open.Instance.Disabled));
        await cut.InvokeAsync(() => open.Instance.OnClick.InvokeAsync());
        StringAssert.Contains(cut.Markup, "全出走馬・結果・払戻");
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("取得を依頼</")).Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Find("[role=status]").TextContent, "受付済み"));
        var path = $"/api/admin/subjects/Horse/{id}/collection/history";
        var job = (await http.GetFromJsonAsync<SubjectCollectionStatus>(path))!.Job;
        await app.Services.GetRequiredService<ProcessingStateStore>().FailJobAsync(job.JobType, job.DeduplicationKey, "一時的な取得失敗");
        cut.WaitForAssertion(() => Assert.IsTrue(completed), TimeSpan.FromSeconds(8));
        StringAssert.Contains(cut.Markup, "一時的な取得失敗");
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("失敗分を再試行</")).Instance.OnClick.InvokeAsync());
        StringAssert.Contains(cut.Markup, "完了済みのレースを維持");
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("取得を依頼</")).Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Find("[role=status]").TextContent, "受付済み"));
        Assert.AreEqual(job.JobId, (await http.GetFromJsonAsync<SubjectCollectionStatus>(path))!.Job.JobId);
    }
}
