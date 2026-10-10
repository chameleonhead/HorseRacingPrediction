using Bunit;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Pages;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;
using System.Net.Http.Json;
using CollectionOperationsPage = HorseRacingPrediction.Api.Web.Components.Pages.CollectionOperations;
using FailureGroupPage = HorseRacingPrediction.Api.Web.Components.Pages.CollectionFailureGroupDetail;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionOperationsComponentTests
{
    [TestMethod]
    public async Task OperationsPage_ShowsRuntimeActionsAndRetainsSnapshotWhenRefreshFails()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new OperationsHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<CollectionOperationsPage>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "ジョブ配送"));
        StringAssert.Contains(cut.Markup, "バックグラウンド処理の稼働状況");
        StringAssert.Contains(cut.Markup, "ジョブ配送");
        StringAssert.Contains(cut.Markup, "新規レース探索");
        StringAssert.Contains(cut.Markup, "定期再取得");
        StringAssert.Contains(cut.Markup, "期限切れ回収");
        StringAssert.Contains(cut.Markup, "過去データ再開");
        StringAssert.Contains(cut.Markup, "配信失敗の確認");
        StringAssert.Contains(cut.Markup, "通知送信");
        StringAssert.Contains(cut.Markup, "処理中");
        StringAssert.Contains(cut.Markup, "メトリクス送信");
        StringAssert.Contains(cut.Markup, "2026/10/08");
        StringAssert.Contains(cut.Markup, "更新 2026/10/08 09:00:00 JST");
        StringAssert.Contains(cut.Markup, "2026/10/08 08:59:40 JST");
        StringAssert.Contains(cut.Markup, "確認が遅れています");
        var boundaryAction = cut.Find("[data-runtime-action='Dispatcher']");
        Assert.IsFalse(boundaryAction.TextContent.Contains("確認が遅れています", StringComparison.Ordinal));
        StringAssert.Contains(cut.Find("[data-runtime-action='DiscoveryPlanner']").TextContent, "確認が遅れています");
        StringAssert.Contains(cut.Find("[data-runtime-action='Alerts']").TextContent, "確認が遅れています");
        Assert.IsFalse(cut.Find("[data-runtime-action='MetricDelivery']").TextContent
            .Contains("確認が遅れています", StringComparison.Ordinal));
        Assert.IsFalse(cut.Find("[data-runtime-action='RefreshPlanner']").TextContent
            .Contains("確認が遅れています", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.RuntimeStatusRequests);

        handler.FailRuntimeStatus = true;
        var refresh = cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("更新</"));
        await cut.InvokeAsync(() => refresh.Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "表示は以前の情報です"));
        StringAssert.Contains(cut.Markup, "ジョブ配送");
        Assert.AreEqual(2, handler.RuntimeStatusRequests);
    }

    [TestMethod]
    public async Task OperationsPage_RequestsRuntimeOnlyWhenMonitoringTabIsActive()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new OperationsHandler
        {
            FirstRuntimeStatusGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<CollectionOperationsPage>();
        cut.WaitForAssertion(() => Assert.HasCount(1, cut.FindComponents<FluentTabs>()));
        var tabs = cut.FindComponent<FluentTabs>();
        await cut.InvokeAsync(() => tabs.Instance.ActiveTabIdChanged.InvokeAsync("failures"));
        await cut.InvokeAsync(() => tabs.Instance.ActiveTabIdChanged.InvokeAsync("monitoring"));
        await handler.FirstRuntimeStatusCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "ジョブ配送"), TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, handler.RuntimeStatusRequests);

        await cut.InvokeAsync(() => tabs.Instance.ActiveTabIdChanged.InvokeAsync("failures"));
        var refresh = cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("更新</"));
        await cut.InvokeAsync(() => refresh.Instance.OnClick.InvokeAsync());
        Assert.AreEqual(2, handler.RuntimeStatusRequests);
    }

    [TestMethod]
    public async Task OperationsPage_TreatsUnsupportedRuntimeStatusEndpointAsUnavailable()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new OperationsHandler { RuntimeStatusNotFound = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<CollectionOperationsPage>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "このAPIでは利用できません"));
        Assert.IsFalse(cut.Markup.Contains("ジョブ配送", StringComparison.Ordinal));
        Assert.AreEqual(1, handler.RuntimeStatusRequests);
    }

    [TestMethod]
    public async Task OperationsPage_GroupsFailuresAndShowsQueueAndBackfillProgress()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new OperationsHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<CollectionOperationsPage>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "要対応の対象"));
        StringAssert.Contains(cut.Markup, "再試行待ち");
        StringAssert.Contains(cut.Markup, "障害対応 (1原因)");
        StringAssert.Contains(cut.Markup, "過去データ収集 (1)");
        var tabs = cut.FindComponent<FluentTabs>();
        await cut.InvokeAsync(() => tabs.Instance.ActiveTabIdChanged.InvokeAsync("backfills"));
        StringAssert.Contains(cut.Markup, "Backfill / 再開待ち / 次回 2026-10-09");
    }

    [TestMethod]
    public async Task OperationsPage_ShowsSafeReviewLabelInsteadOfGuessingUnclassifiedBatchKind()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new OperationsHandler
        {
            BackfillRecovery = new(CollectionBatchKind.Unknown, CollectionBatchRecoveryState.NeedsReview,
                null, "MixedOrUnsupportedReasons")
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<CollectionOperationsPage>();
        await cut.InvokeAsync(() => cut.FindComponent<FluentTabs>().Instance.ActiveTabIdChanged.InvokeAsync("backfills"));
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "処理種別を確認してください"));
        StringAssert.Contains(cut.Markup, "関連情報の処理種別が一致しません。");
        Assert.IsFalse(cut.Markup.Contains("MixedOrUnsupportedReasons", StringComparison.Ordinal));
        Assert.IsFalse(cut.Markup.Contains("Backfill / 再開待ち", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Backfill_ValidatesMonthBeforeSendingRequest()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new OperationsHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<CollectionOperationsPage>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "過去データ収集 (1)"));

        var month = cut.FindComponents<FluentTextField>().Single(x => x.Instance.Label?.ToString() == "月");
        await cut.InvokeAsync(() => month.Instance.ValueChanged.InvokeAsync("13"));
        var start = cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("収集を開始</"));
        await cut.InvokeAsync(() => start.Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "正しい年と月を入力してください"));
        Assert.AreEqual(0, handler.BackfillRequests);
    }

    [TestMethod]
    public async Task FailureGroupPage_ShowsCauseUrlAndBulkRecoveryAction()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new OperationsHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<FailureGroupPage>(parameters => parameters
            .Add(x => x.GroupKey, "race-result|Failed|UnexpectedPage"));

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "想定と異なるページを検出しました"));
        StringAssert.Contains(cut.Markup, "https://www.jra.go.jp/JRADB/result/R1");
        StringAssert.Contains(cut.Markup, "対象をまとめて再取得");
        StringAssert.Contains(cut.Markup, "レース結果 ・ JRA");
    }

    [TestMethod]
    public async Task FailureGroupPage_HidesRecoveryActionsForSubjectIdentificationFailure()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new SubjectIdentificationHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<FailureGroupPage>(parameters => parameters
            .Add(x => x.GroupKey, "horse-profile|Failed|SubjectNotIdentified"));

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "自動再取得は行いません"));
        Assert.IsFalse(cut.Markup.Contains("対象をまとめて再取得", StringComparison.Ordinal));
        Assert.IsFalse(cut.Markup.Contains("選択した対象を再取得", StringComparison.Ordinal));
        Assert.IsFalse(cut.Markup.Contains("を選択\"", StringComparison.Ordinal));
    }

    private sealed class SubjectIdentificationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var notificationId = Guid.NewGuid();
            var resource = new ResourceKey(CollectionResourceType.Horse, "JRA", "horse-1");
            var group = new CollectionFailureGroup("horse-profile|Failed|SubjectNotIdentified",
                new("horse-profile"), CollectionTaskStatus.Failed, "SubjectNotIdentified",
                "同定不能: 公開検索に一致候補が複数あります。", 1,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [notificationId], [resource]);
            var page = new CollectionFailureGroupPage(group, 1, 1, 50, null,
                [new(notificationId, Guid.NewGuid(), resource, new("horse-profile"),
                    CollectionTaskStatus.Failed, group.ErrorCode, group.ErrorMessage, 1,
                    DateTimeOffset.UtcNow, null, null, null,
                    "SubjectIdentification:MultipleCandidates", null, null)]);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = JsonContent.Create(new { page }) });
        }
    }

    private sealed class OperationsHandler : HttpMessageHandler
    {
        public int BackfillRequests { get; private set; }
        public int RuntimeStatusRequests { get; private set; }
        public bool FailRuntimeStatus { get; set; }
        public bool RuntimeStatusNotFound { get; set; }
        public BackfillBatchRecovery? BackfillRecovery { get; set; } = new(
            CollectionBatchKind.Backfill, CollectionBatchRecoveryState.Ready,
            new DateOnly(2026, 10, 9), null);
        public TaskCompletionSource<bool>? FirstRuntimeStatusGate { get; set; }
        public TaskCompletionSource<bool> FirstRuntimeStatusCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/admin/collection/operations/runtime-status")
            {
                RuntimeStatusRequests++;
                if (RuntimeStatusRequests == 1 && FirstRuntimeStatusGate is { } gate)
                    return WaitForRuntimeStatusGateAsync(gate.Task, cancellationToken);
                if (RuntimeStatusNotFound)
                    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
                if (FailRuntimeStatus)
                    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));

                return Ok(new GetCollectionRuntimeStatusResponse(CreateRuntimeStatus()));
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/backfills", StringComparison.Ordinal))
            {
                BackfillRequests++;
                return Ok(new
                {
                    batch = new BackfillBatchSnapshot("jra:2026-09", new(2026, 9, 1), new(2026, 9, 30),
                    30, 1, 1, 0, 0, 0, [], DateTimeOffset.UtcNow, null)
                });
            }

            object value = path switch
            {
                "/api/v2/admin/collection/operations/dashboard" => new
                {
                    dashboard = new CollectionOperationsDashboard(
                    new CollectionProgressSnapshot(new Dictionary<CollectionResourceType, int> { [CollectionResourceType.Race] = 10 },
                        new Dictionary<CollectionStateStatus, int> { [CollectionStateStatus.Failed] = 2 },
                        new Dictionary<CollectionLane, int> { [CollectionLane.Realtime] = 3 },
                        new Dictionary<int, int> { [100] = 3 }, new Dictionary<string, int>(), 2),
                    [new CollectionFailureGroup("race-result|Failed|UnexpectedPage", new("race-result"),
                        CollectionTaskStatus.Failed, "UnexpectedPage", "別ページ", 2,
                        DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow,
                        [Guid.NewGuid(), Guid.NewGuid()], [new(CollectionResourceType.RaceResult, "JRA", "R1")])],
                    [new BackfillBatchSnapshot("jra:2026-08", new(2026, 8, 1), new(2026, 8, 31),
                        31, 31, 0, 0, 30, 1, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                        BackfillRecovery)], DateTimeOffset.UtcNow)
                },
                "/api/v2/admin/collection/operations/progress" => new
                {
                    progress = new CollectionProgressSnapshot(
                    new Dictionary<CollectionResourceType, int> { [CollectionResourceType.Race] = 10 },
                    new Dictionary<CollectionStateStatus, int> { [CollectionStateStatus.Failed] = 2 },
                    new Dictionary<CollectionLane, int> { [CollectionLane.Realtime] = 3 },
                    new Dictionary<int, int> { [100] = 3 }, new Dictionary<string, int>(), 2)
                },
                "/api/v2/admin/collection/failure-notification-groups" => new
                {
                    groups = new[]
                {
                    new CollectionFailureGroup("race-result|Failed|UnexpectedPage", new("race-result"),
                        CollectionTaskStatus.Failed, "UnexpectedPage", "別ページ", 2,
                        DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow,
                        [Guid.NewGuid(), Guid.NewGuid()], [new(CollectionResourceType.RaceResult, "JRA", "R1")]),
                }
                },
                "/api/v2/admin/collection/failure-notification-groups/race-result%7CFailed%7CUnexpectedPage" =>
                    new { page = FailurePage() },
                "/api/v2/admin/collection/backfill-batches" => new
                {
                    batches = new[]
                {
                    new BackfillBatchSnapshot("jra:2026-08", new(2026, 8, 1), new(2026, 8, 31),
                        31, 31, 0, 0, 30, 1, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                }
                },
                _ => Array.Empty<object>(),
            };
            return Ok(value);
        }

        private static CollectionFailureGroupPage FailurePage()
        {
            var notificationId = Guid.NewGuid();
            var resource = new ResourceKey(CollectionResourceType.RaceResult, "JRA", "R1");
            var group = new CollectionFailureGroup("race-result|Failed|UnexpectedPage", new("race-result"),
                CollectionTaskStatus.Failed, "UnexpectedPage", "別のレースページを検出", 1,
                DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow, [notificationId], [resource]);
            return new(group, 1, 1, 50, null,
                [new(notificationId, Guid.NewGuid(), resource, new("race-result"), CollectionTaskStatus.Failed,
                    "UnexpectedPage", "別のレースページを検出", 3, DateTimeOffset.UtcNow,
                    "https://www.jra.go.jp/JRADB/result/R1", null, 200, "RaceResult:R2", Guid.NewGuid(), "lambda-1")]);
        }

        private static Task<HttpResponseMessage> Ok(object value) => Task.FromResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = JsonContent.Create(value) });

        private async Task<HttpResponseMessage> WaitForRuntimeStatusGateAsync(Task<bool> gate,
            CancellationToken cancellationToken)
        {
            try
            {
                await gate.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                FirstRuntimeStatusCancelled.TrySetResult(true);
                throw;
            }

            return await Ok(new GetCollectionRuntimeStatusResponse(CreateRuntimeStatus()));
        }

        private static CollectionRuntimeStatusDto CreateRuntimeStatus()
        {
            var now = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
            var actions = Enum.GetValues<CollectionRuntimeAction>().Select(action =>
            {
                var enabled = action != CollectionRuntimeAction.RefreshPlanner;
                TimeSpan? interval = action == CollectionRuntimeAction.MetricDelivery
                    ? null : TimeSpan.FromSeconds(1);
                DateTimeOffset? lastCompleted = action switch
                {
                    CollectionRuntimeAction.Dispatcher => now.AddSeconds(-33),
                    CollectionRuntimeAction.DiscoveryPlanner => now.AddSeconds(-34),
                    CollectionRuntimeAction.RefreshPlanner => now.AddHours(-1),
                    CollectionRuntimeAction.Alerts => null,
                    CollectionRuntimeAction.MetricDelivery => now.AddHours(-1),
                    _ => now.AddSeconds(-30),
                };
                var state = !enabled ? CollectionRuntimeState.Disabled
                    : action == CollectionRuntimeAction.Dispatcher ? CollectionRuntimeState.Running
                    : CollectionRuntimeState.Waiting;
                CollectionRuntimeReason? reason = action == CollectionRuntimeAction.Dispatcher || !enabled
                    ? null : CollectionRuntimeReason.NoDueWork;
                return new CollectionRuntimeActionStatusDto(action, enabled, interval, state, reason,
                    now.AddMinutes(-1), lastCompleted, lastCompleted, now.AddSeconds(-20),
                    500, 8, 2, 1, 3, 1, 0);
            }).ToArray();
            return new(Guid.Parse("8c02189c-2c33-4fd3-a56d-4d47df2d45d9"), now.AddHours(-1), now, actions);
        }
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
