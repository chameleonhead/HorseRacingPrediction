using System.Net;
using System.Net.Http.Json;
using Bunit;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
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

    [TestMethod]
    public async Task SubjectRepair_RendersFourSubjects_FilterAndRetryReadyWithoutMergeCandidate()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new SubjectRepairHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Settings>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "主体識別情報の補正"));
        StringAssert.Contains(cut.Markup, "競走馬");
        StringAssert.Contains(cut.Markup, "騎手");
        StringAssert.Contains(cut.Markup, "調教師");
        StringAssert.Contains(cut.Markup, "馬主");
        StringAssert.Contains(cut.Markup, "RetryReady");
        Assert.IsFalse(cut.Markup.Contains("MergeReady", StringComparison.Ordinal));

        await cut.InvokeAsync(() => cut.Find("select[aria-label='主体種別で絞り込み']").Change("Jockey"));
        StringAssert.Contains(cut.Markup, "jockey-1");
        Assert.IsFalse(cut.Markup.Contains("horse-1", StringComparison.Ordinal));

        var checkbox = cut.FindComponents<FluentCheckbox>()
            .First(x => x.Markup.Contains("jockey-1", StringComparison.Ordinal));
        await cut.InvokeAsync(() => checkbox.Instance.CheckStateChanged.InvokeAsync(true));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("選択した補正を確認")).Instance.OnClick.InvokeAsync());
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("補正して再収集")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "収集タスク 1 件を作成しました"));
        Assert.AreEqual(Guid.Parse("00000000-0000-0000-0000-000000000002"), handler.Executed.Single().NotificationId);
        Assert.IsNull(handler.Executed.Single().CorrectionUrl);
    }

    [TestMethod]
    public async Task SubjectRepair_BlockedParameterlessUrlRequiresValidCorrectionUrl()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new SubjectRepairHandler(blockedOnly: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Settings>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "要確認"));
        var blocked = cut.FindComponents<FluentCheckbox>()
            .First(x => x.Markup.Contains("trainer-1", StringComparison.Ordinal));
        Assert.IsTrue(blocked.Instance.Disabled);

        var url = cut.FindComponents<FluentTextField>()
            .First(x => x.Markup.Contains("trainer-1", StringComparison.Ordinal));
        await cut.InvokeAsync(() => url.Instance.ValueChanged.InvokeAsync("https://www.jra.go.jp/JRADB/accessD.html?CNAME=trainer-identity"));
        cut.WaitForAssertion(() => Assert.IsFalse(cut.FindComponents<FluentCheckbox>()
            .First(x => x.Markup.Contains("trainer-1", StringComparison.Ordinal)).Instance.Disabled));

        blocked = cut.FindComponents<FluentCheckbox>()
            .First(x => x.Markup.Contains("trainer-1", StringComparison.Ordinal));
        await cut.InvokeAsync(() => blocked.Instance.CheckStateChanged.InvokeAsync(true));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("選択した補正を確認")).Instance.OnClick.InvokeAsync());
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("補正して再収集")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => Assert.HasCount(1, handler.Executed));
        Assert.AreEqual("https://www.jra.go.jp/JRADB/accessD.html?CNAME=trainer-identity", handler.Executed.Single().CorrectionUrl);
    }

    [TestMethod]
    public async Task SubjectRepair_EmptyAndErrorStatesRemainActionable()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new SubjectEmptyHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<Settings>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "現在、要対応の主体識別失敗はありません"));
        StringAssert.Contains(cut.Markup, "再読込");

        var (errorApplication, errorOriginal) = await TestApplicationFactory.CreateAsync();
        await using var errorApp = errorApplication;
        using var errorHttp = new HttpClient(new SubjectFailureHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var errorContext = CreateContext(errorApplication.Services, errorHttp);
        var errorCut = errorContext.Render<Settings>();
        errorCut.WaitForAssertion(() => StringAssert.Contains(errorCut.Markup, "補正候補を処理できませんでした"));
        StringAssert.Contains(errorCut.Markup, "時間をおいて再読込");
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

    private sealed class SubjectRepairHandler(bool blockedOnly = false) : HttpMessageHandler
    {
        public List<ExecuteSubjectIdentificationRepairItem> Executed { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.EndsWith("subject-identification", StringComparison.Ordinal) == true)
            {
                var items = blockedOnly
                    ? new[] { Subject("trainer-1", ResourceType.Trainer, "Blocked", false, null, "URLが必要です。", 3) }
                    : new[]
                    {
                        Subject("horse-1", ResourceType.Horse, "RetryReady", true, "https://www.jra.go.jp/JRADB/accessU.html?CNAME=horse-identity", null, 1),
                        Subject("jockey-1", ResourceType.Jockey, "RetryReady", true, null, null, 2),
                        Subject("trainer-1", ResourceType.Trainer, "RetryReady", true, null, null, 3),
                        Subject("owner-1", ResourceType.Owner, "RetryReady", true, null, null, 4)
                    };
                return Ok(new SubjectIdentificationRepairPreviewResponse(items));
            }
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.EndsWith("subject-identification/execute", StringComparison.Ordinal) == true)
            {
                var body = await request.Content!.ReadFromJsonAsync<ExecuteSubjectIdentificationRepairRequest>(cancellationToken: cancellationToken);
                Executed.AddRange(body!.Items);
                return Accepted(new ExecuteSubjectIdentificationRepairResponse(body.Items.Count, body.Items.Count, 0,
                    body.Items.Select(x => Guid.NewGuid()).ToArray()));
            }
            return Ok(new HorseIdentityRepairPreviewResponse("20260913-jra-horse-identity-repair", []));
        }

        private static SubjectIdentificationRepairCandidateResponse Subject(string id, ResourceType type,
            string evaluation, bool safe, string? suggested, string? blocked, int ordinal) =>
            new(Guid.Parse($"00000000-0000-0000-0000-{ordinal:000000000000}"), Guid.NewGuid(), type, id,
                $"{type.ToString().ToLowerInvariant()}-profile", "識別できませんでした", DateTimeOffset.UtcNow,
                evaluation, safe, blocked, suggested);
    }

    private sealed class SubjectEmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(
                request.RequestUri?.AbsolutePath.EndsWith("subject-identification", StringComparison.Ordinal) == true
                    ? Ok(new SubjectIdentificationRepairPreviewResponse([]))
                    : Ok(new HorseIdentityRepairPreviewResponse("20260913-jra-horse-identity-repair", [])));
    }

    private sealed class SubjectFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new HttpRequestException("offline");
    }

    private static HttpResponseMessage Ok(object value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static HttpResponseMessage Accepted(object value) =>
        new(HttpStatusCode.Accepted) { Content = JsonContent.Create(value) };
}
