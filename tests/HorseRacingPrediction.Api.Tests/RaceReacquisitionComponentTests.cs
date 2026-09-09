using System.Net.Http.Json;
using Bunit;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Shared;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public class RaceReacquisitionComponentTests
{
    [TestMethod]
    public async Task Confirmation_Request_StatusAndCompletionAreVisible()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var raceId = "race-" + Guid.NewGuid();
        (await http.PostAsJsonAsync("/api/races", new { raceId, raceDate = new DateOnly(2026, 9, 6), racecourseCode = "中山", raceNumber = 6, raceName = "メイクデビュー中山" })).EnsureSuccessStatusCode();
        var race = (await http.GetFromJsonAsync<RaceResponse>($"/api/races/{raceId}"))!;
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton(new AdminApiClient(http, new AdminApiBaseAddressResolver(app.Services.GetRequiredService<IServer>()),
            Options.Create(new ApiKeyOptions { Key = TestApplicationFactory.TestApiKey })));
        var completed = false;
        var cut = context.Render<RaceReacquisitionAction>(p => p.Add(x => x.Race, race).Add(x => x.OnCompleted, () => completed = true));
        var open = cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("JRAから再取得"));
        cut.WaitForAssertion(() => Assert.IsFalse(open.Instance.Disabled));
        await cut.InvokeAsync(() => open.Instance.OnClick.InvokeAsync());
        Assert.IsFalse(cut.FindComponent<FluentDialog>().Instance.Hidden);
        StringAssert.Contains(cut.Markup, "変更が見つかった項目はすべてJRAの情報で更新します");
        var submit = cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("再取得を依頼</"));
        await cut.InvokeAsync(() => submit.Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Find("[role=status]").TextContent, "受付済み"));
        Assert.IsTrue(cut.FindComponent<FluentDialog>().Instance.Hidden);
        var store = app.Services.GetRequiredService<ProcessingStateStore>();
        var job = (await store.GetRaceReacquisitionAsync(raceId))!;
        Assert.AreEqual(JobNavigation.DetailUrl(job.JobId), cut.Find("a").GetAttribute("href"));
        await store.CompleteJobAsync(job.JobType, job.DeduplicationKey);
        cut.WaitForAssertion(() => Assert.IsTrue(completed), TimeSpan.FromSeconds(8));
        StringAssert.Contains(cut.Find("[role=status]").TextContent, "完了");
    }
}
