using System.Net.Http.Json;
using Bunit;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Pages;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;
using CollectionOperationsPage = HorseRacingPrediction.Api.Web.Components.Pages.CollectionOperations;

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
                "/api/admin/collection/backfills" => new[]
                {
                    new BackfillBatchSnapshot("jra:2026-08", new(2026, 8, 1), new(2026, 8, 31),
                        31, 31, 0, 0, 30, 1, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                },
                _ => Array.Empty<object>(),
            };
            return Ok(value);
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
