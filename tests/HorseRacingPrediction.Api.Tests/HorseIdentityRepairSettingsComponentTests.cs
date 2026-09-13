using System.Net;
using System.Net.Http.Json;
using Bunit;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Pages;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityRepairSettingsComponentTests
{
    [TestMethod]
    public async Task SafeAndBlockedCandidates_OnlySafeCandidateCanBeApplied()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new RepairHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Settings>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Find(".repair-summary").TextContent, "安全 1 件"));
        StringAssert.Contains(cut.Markup, "要確認");
        StringAssert.Contains(cut.Markup, "RaceEntryが残っています");
        Assert.HasCount(2, cut.FindComponents<FluentCheckbox>().Where(x => !x.Instance.Disabled));

        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("安全な候補をすべて選択")).Instance.OnClick.InvokeAsync());
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("選択した候補を補正")).Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Find("#repair-confirm-title").TextContent, "選択した1件を補正しますか"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("補正を実行")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "収集タスク 2 件を無効化"));
        CollectionAssert.AreEqual(new[] { "safe-1" }, handler.AppliedCandidateIds);
    }

    [TestMethod]
    public async Task EmptyPreview_ShowsActionableEmptyState()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new EmptyRepairHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Settings>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "現在、補正できる候補はありません"));
        StringAssert.Contains(cut.Markup, "再読込");
    }

    [TestMethod]
    public async Task LoadFailure_ShowsRecoveryGuidance()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new FailureHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Settings>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "補正候補を処理できませんでした"));
        StringAssert.Contains(cut.Markup, "時間をおいて再読込");
    }

    private static BunitContext CreateContext(IServiceProvider services, HttpClient http)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton(new AdminApiClient(http,
            new AdminApiBaseAddressResolver(services.GetRequiredService<IServer>()),
            Options.Create(new ApiKeyOptions { Key = TestApplicationFactory.TestApiKey })));
        return context;
    }

    private sealed class RepairHandler : HttpMessageHandler
    {
        public string[] AppliedCandidateIds { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                var candidates = AppliedCandidateIds.Length == 0
                    ? new[]
                    {
                        new HorseIdentityRepairCandidateResponse("safe-1", "horse-old", "horse-new",
                            "pw01dud002023106188/45", "race-1", "entry-1", true, null,
                            "ビッグヒーロー", "ビッグヒーロー", "テストレース", 2),
                        new HorseIdentityRepairCandidateResponse("blocked-1", "horse-blocked", "horse-target",
                            "pw01dud002007101324/2A", "race-2", "entry-2", false,
                            "RaceEntryが残っています。", "同名馬", "同名馬", "別レース", 1)
                    }
                    : [];
                return Ok(new HorseIdentityRepairPreviewResponse(
                    "20260913-jra-horse-identity-repair", candidates));
            }
            var body = await request.Content!.ReadFromJsonAsync<ApplyHorseIdentityRepairRequest>(
                cancellationToken: cancellationToken);
            AppliedCandidateIds = body!.CandidateIds.ToArray();
            return Ok(new ApplyHorseIdentityRepairResponse(
                "20260913-jra-horse-identity-repair", 1, 0, 2, 0));
        }
    }

    private sealed class EmptyRepairHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(Ok(
            new HorseIdentityRepairPreviewResponse("20260913-jra-horse-identity-repair", [])));
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new HttpRequestException("offline");
    }

    private static HttpResponseMessage Ok(object value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}
