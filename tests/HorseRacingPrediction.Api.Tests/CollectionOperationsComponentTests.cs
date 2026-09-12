using System.Net.Http.Json;
using Bunit;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Pages;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;
using CollectionOperationsPage = HorseRacingPrediction.Api.Web.Components.Pages.CollectionOperations;
using FailureGroupPage = HorseRacingPrediction.Api.Web.Components.Pages.CollectionFailureGroupDetail;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionOperationsComponentTests
{
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

    private sealed class OperationsHandler : HttpMessageHandler
    {
        public int BackfillRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/backfills", StringComparison.Ordinal))
            {
                BackfillRequests++;
                return Ok(new BackfillBatchSnapshot("jra:2026-09", new(2026, 9, 1), new(2026, 9, 30),
                    30, 1, 1, 0, 0, 0, [], DateTimeOffset.UtcNow, null));
            }

            object value = path switch
            {
                "/api/admin/collection/dashboard" => new CollectionOperationsDashboard(
                    new CollectionProgressSnapshot(new Dictionary<ResourceType, int> { [ResourceType.Race] = 10 },
                        new Dictionary<CollectionStateStatus, int> { [CollectionStateStatus.Failed] = 2 },
                        new Dictionary<CollectionLane, int> { [CollectionLane.Realtime] = 3 },
                        new Dictionary<int, int> { [100] = 3 }, new Dictionary<string, int>(), 2),
                    [new CollectionFailureGroup("race-result|Failed|UnexpectedPage", new("race-result"),
                        CollectionTaskStatus.Failed, "UnexpectedPage", "別ページ", 2,
                        DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow,
                        [Guid.NewGuid(), Guid.NewGuid()], [new(ResourceType.RaceResult, "JRA", "R1")])],
                    [new BackfillBatchSnapshot("jra:2026-08", new(2026, 8, 1), new(2026, 8, 31),
                        31, 31, 0, 0, 30, 1, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)], DateTimeOffset.UtcNow),
                "/api/admin/collection/progress" => new CollectionProgressSnapshot(
                    new Dictionary<ResourceType, int> { [ResourceType.Race] = 10 },
                    new Dictionary<CollectionStateStatus, int> { [CollectionStateStatus.Failed] = 2 },
                    new Dictionary<CollectionLane, int> { [CollectionLane.Realtime] = 3 },
                    new Dictionary<int, int> { [100] = 3 }, new Dictionary<string, int>(), 2),
                "/api/admin/collection/failure-notifications/groups" => new[]
                {
                    new CollectionFailureGroup("race-result|Failed|UnexpectedPage", new("race-result"),
                        CollectionTaskStatus.Failed, "UnexpectedPage", "別ページ", 2,
                        DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow,
                        [Guid.NewGuid(), Guid.NewGuid()], [new(ResourceType.RaceResult, "JRA", "R1")]),
                },
                "/api/admin/collection/failure-notifications/groups/race-result%7CFailed%7CUnexpectedPage" =>
                    FailurePage(),
                "/api/admin/collection/backfills" => new[]
                {
                    new BackfillBatchSnapshot("jra:2026-08", new(2026, 8, 1), new(2026, 8, 31),
                        31, 31, 0, 0, 30, 1, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                },
                _ => Array.Empty<object>(),
            };
            return Ok(value);
        }

        private static CollectionFailureGroupPage FailurePage()
        {
            var notificationId = Guid.NewGuid();
            var resource = new ResourceKey(ResourceType.RaceResult, "JRA", "R1");
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
