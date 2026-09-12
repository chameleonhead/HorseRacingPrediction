using Bunit;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Pages;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;
using BatchDetailPage = HorseRacingPrediction.Api.Web.Components.Pages.CollectionExecutionBatchDetail;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class JobDetailComponentTests
{
    [TestMethod]
    public async Task Detail_ShowsRequestAndTaskHistory_AndCancelsOnlyActiveTask()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new JobDetailHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = RenderDetail(context);

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "依頼履歴 (30)"));
        StringAssert.Contains(cut.Markup, "過去データ収集");
        StringAssert.Contains(cut.Markup, "タスク履歴 (2)");
        StringAssert.Contains(cut.Markup, "実行タスクの履歴");
        StringAssert.Contains(cut.Markup, "障害履歴 (2)");
        StringAssert.Contains(cut.Markup, "対応中");
        StringAssert.Contains(cut.Markup, "解決済み");
        StringAssert.Contains(cut.Markup, "実行バッチ");
        StringAssert.Contains(cut.Markup, "/jobs/execution-batches/33333333-3333-3333-3333-333333333333");
        Assert.AreEqual(1, cut.FindComponents<FluentButton>()
            .Count(x => x.Markup.Contains("このタスクを取り消す</")));

        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("このタスクを取り消す</")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "を取り消しました"));
        Assert.AreEqual(1, handler.CancelRequests);
    }

    [TestMethod]
    public async Task RequestHistory_PagesIndependently_AndKeepsLatestTaskSummary()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new JobDetailHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = RenderDetail(context);
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "1 / 2 ページ"));

        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains(">次へ</")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "2 / 2 ページ"));
        Assert.AreEqual(2, handler.RequestHistoryPage);
        Assert.AreEqual(1, handler.TaskHistoryPage);
        StringAssert.Contains(cut.Markup, "リアルタイム");
        StringAssert.Contains(cut.Markup, "90");
    }

    [TestMethod]
    public async Task Retry_ShowsReceipt_AndExplainsReusedActiveTask()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new JobDetailHandler { CreatedTask = false };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = RenderDetail(context);
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "通常の方法で再取得"));

        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("通常の方法で再取得</")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "実行中のタスク 22222222 にまとめました"));
        Assert.AreEqual(1, handler.ManualRequests);
    }

    [TestMethod]
    public async Task ExplicitUrlRetry_WhenConnectionFails_PreservesInputAndShowsRecoveryMessage()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new JobDetailHandler { FailManualRequest = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = RenderDetail(context);
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "取得先を指定して再取得"));
        var field = cut.FindComponents<FluentTextField>()
            .Single(x => x.Instance.Label?.ToString() == "取得先URL");
        await cut.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync("https://example.test/resource"));

        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("指定したURLで再取得</")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "入力内容を保持しています"));
        Assert.AreEqual("https://example.test/resource", field.Instance.Value);
    }

    [TestMethod]
    public async Task ReturnLink_PreservesSafeListContext_AndRejectsExternalUrl()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new JobDetailHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/jobs/Horse/jra/H001/horse-profile?returnUrl=%2Fjobs%3Fview%3Dwaiting%26page%3D3");
        var preserved = RenderDetail(context);
        preserved.WaitForAssertion(() => Assert.AreEqual("/jobs?view=waiting&page=3",
            preserved.Find("a.back-link").GetAttribute("href")));

        navigation.NavigateTo("/jobs/Horse/jra/H001/horse-profile?returnUrl=%2F%2Fexample.test%2Fjobs");
        var rejected = RenderDetail(context);
        rejected.WaitForAssertion(() => Assert.AreEqual("/jobs",
            rejected.Find("a.back-link").GetAttribute("href")));
    }

    [TestMethod]
    public async Task ExecutionBatchDetail_ShowsTraceIdentifiersAndLinksToEachResource()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new JobDetailHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<BatchDetailPage>(parameters => parameters
            .Add(x => x.ExecutionBatchId, Guid.Parse("33333333-3333-3333-3333-333333333333")));

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "SQS message ID"));
        StringAssert.Contains(cut.Markup, "sqs-message");
        StringAssert.Contains(cut.Markup, "lambda-request");
        StringAssert.Contains(cut.Markup, "H001");
        StringAssert.Contains(cut.Markup, "/jobs/Horse/JRA/H001/horse-profile");
    }

    private static IRenderedComponent<JobDetail> RenderDetail(BunitContext context) =>
        context.Render<JobDetail>(parameters => parameters
            .Add(x => x.ResourceTypeName, "Horse")
            .Add(x => x.Provider, "jra")
            .Add(x => x.ResourceId, "H001")
            .Add(x => x.DefinitionId, "horse-profile"));

    private sealed class JobDetailHandler : HttpMessageHandler
    {
        private static readonly ResourceKey Resource = new(ResourceType.Horse, "JRA", "H001");
        private static readonly CollectionDefinitionId Definition = new("horse-profile");
        private static readonly Guid ActiveTaskId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid ReceiptTaskId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid ExecutionBatchId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public bool CreatedTask { get; init; } = true;
        public bool FailManualRequest { get; init; }
        public int ManualRequests { get; private set; }
        public int CancelRequests { get; private set; }
        public int RequestHistoryPage { get; private set; } = 1;
        public int TaskHistoryPage { get; private set; } = 1;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/requests"))
            {
                ManualRequests++;
                if (FailManualRequest) throw new HttpRequestException("offline");
                return await Ok(new CollectionRequestReceipt(Guid.NewGuid(), ReceiptTaskId, CreatedTask));
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/cancel"))
            {
                CancelRequests++;
                return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
            }

            if (request.RequestUri!.AbsolutePath.Contains("/execution-batches/", StringComparison.Ordinal))
            {
                var batchNow = DateTimeOffset.UtcNow;
                return await Ok(new HorseRacingPrediction.CollectionOperations.CollectionPlatform.CollectionExecutionBatchDetail(
                    ExecutionBatchId, Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    "sqs-message", "lambda-request", 1, batchNow, batchNow.AddSeconds(2),
                    [new(ActiveTaskId, Resource, Definition, CollectionTaskStatus.Succeeded,
                        CollectionAttemptResult.Succeeded, 1, 1, batchNow, batchNow.AddSeconds(2))]));
            }

            var now = DateTimeOffset.UtcNow;
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query);
            RequestHistoryPage = int.TryParse(query["requestHistoryPage"], out var requestPage) ? requestPage : 1;
            TaskHistoryPage = int.TryParse(query["taskHistoryPage"], out var taskPage) ? taskPage : 1;
            var latestTask = new CollectionTaskSummary(ActiveTaskId, Resource, Definition,
                CollectionTaskStatus.Pending, CollectionLane.Realtime, 90, 1, now, 0);
            var detail = new CollectionResourceDetail(
                new(Resource, Definition, 1, 1, now.AddHours(-1), null, CollectionStateStatus.Pending),
                [],
                [new(Guid.NewGuid(), 1, CollectionReason.Backfill, now.AddHours(-2), null, "2026-09")],
                [
                    new(ActiveTaskId, Resource, Definition, CollectionTaskStatus.Pending,
                        CollectionLane.Normal, 50, 1, now, 0),
                    new(Guid.NewGuid(), Resource, Definition, CollectionTaskStatus.Succeeded,
                        CollectionLane.Normal, 50, 1, now.AddDays(-1), 1),
                ],
                [new(Guid.NewGuid(), ActiveTaskId, 1, now, now.AddSeconds(2), CollectionAttemptResult.Succeeded,
                    null, null, null, null, null, null, ExecutionBatchId, Guid.NewGuid(),
                    "sqs-message", "lambda-request", 1, 2)],
                RequestTotal: 30, TaskTotal: 2, AttemptTotal: 1, LatestTask: latestTask,
                TaskHistoryPage: TaskHistoryPage, AttemptHistoryPage: 1,
                Failures:
                [
                    new(Guid.NewGuid(), Guid.NewGuid(), Resource, Definition, CollectionTaskStatus.Failed,
                        "Old", "old failure", 1, now.AddDays(-2), CollectionFailureResolutionStatus.Resolved,
                        ResolvedAt: now.AddDays(-1)),
                    new(Guid.NewGuid(), Guid.NewGuid(), Resource, Definition, CollectionTaskStatus.Failed,
                        "Current", "recovering", 1, now.AddHours(-1),
                        CollectionFailureResolutionStatus.RecoveryInProgress, ActiveTaskId, now),
                ]);
            return await Ok(detail);
        }

        private static Task<HttpResponseMessage> Ok(object value) => Task.FromResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = JsonContent.Create(value) });
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
}
