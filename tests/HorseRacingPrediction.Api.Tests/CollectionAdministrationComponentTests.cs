using Bunit;
using System.Net.Http.Json;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Pages;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionAdministrationComponentTests
{
    [TestMethod]
    public async Task LoadFailure_ShowsErrorState()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new FailureHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Jobs>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "操作を完了できませんでした"));
        Assert.AreEqual(1, cut.FindAll("[role='alert']").Count);
    }

    [TestMethod]
    public async Task EmptyPlatform_ShowsEmptyStateAndBulkRequiresPreview()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Jobs>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "表示する収集処理はありません"));
        await ClickFluentButtonAsync(cut, "収集を依頼");
        await ClickButtonAsync(cut, "複数をまとめて再取得");
        await SelectAsync(cut, "対象の選び方", "SpecificResources");
        cut.WaitForAssertion(() => Assert.IsTrue(cut.FindComponents<FluentButton>()
            .Last(x => x.Markup.Contains("再取得を依頼</")).Instance.Disabled));
        StringAssert.Contains(cut.Markup, "確認後に対象が増えることはありません");
    }

    [TestMethod]
    public async Task ResourceSelection_LinksToIndependentDetailPage()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new ResourceHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Jobs>();
        await ClickButtonAsync(cut, "待機中");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "H001"));
        StringAssert.Contains(cut.Markup, "競走馬情報の収集");
        StringAssert.Contains(cut.Markup, "登録待ち");
        var link = cut.FindAll("a").Single(x => x.TextContent.Contains("H001"));
        StringAssert.Contains(link.GetAttribute("href"), "/jobs/Horse/jra/H001/horse-profile");

        var detailCut = context.Render<JobDetail>(parameters => parameters
            .Add(x => x.ResourceTypeName, "Horse").Add(x => x.Provider, "jra")
            .Add(x => x.ResourceId, "H001").Add(x => x.DefinitionId, "horse-profile"));
        detailCut.WaitForAssertion(() => StringAssert.Contains(detailCut.Markup, "通常の方法で再取得"));
        StringAssert.Contains(detailCut.Markup, "収集処理の概要");
        StringAssert.Contains(detailCut.Markup, "保存済みの取得先候補はありません");
        StringAssert.Contains(detailCut.Markup, "実行履歴はまだありません");
        await detailCut.InvokeAsync(() => detailCut.FindComponents<FluentButton>()
            .First(x => x.Markup.Contains("通常の方法で再取得</")).Instance.OnClick.InvokeAsync());
        Assert.AreEqual(1, handler.ManualRequests);
        await ClickFluentButtonAsync(cut, "収集を依頼");
        await ClickButtonAsync(cut, "複数をまとめて再取得");
        await SelectAsync(cut, "対象の選び方", "SpecificResources");
        var ids = cut.FindComponents<FluentTextField>()
            .Single(x => x.Instance.Label?.ToString() == "対象ID（カンマ区切り）");
        await cut.InvokeAsync(() => ids.Instance.ValueChanged.InvokeAsync("H001"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("対象を確認</")).Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => Assert.IsFalse(cut.FindComponents<FluentButton>()
            .Last(x => x.Markup.Contains("再取得を依頼</")).Instance.Disabled));
        Assert.AreEqual(1, handler.Previews);
    }

    [TestMethod]
    public async Task Search_WithJapaneseResourceName_FiltersAndCanBeCleared()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new ResourceHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Jobs>();
        await ClickButtonAsync(cut, "待機中");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "H001"));
        var search = cut.FindComponents<FluentTextField>()
            .Single(x => x.Instance.Placeholder?.ToString()?.Contains("対象ID") == true);
        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("騎手"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("絞り込む</")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "表示する収集処理はありません"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("条件をクリア</")).Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "H001"));
    }

    [TestMethod]
    public async Task QuickAction_CreatesHorseCollectionRequest()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new ResourceHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Jobs>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "収集状況"));
        await ClickFluentButtonAsync(cut, "収集を依頼");
        await ClickButtonAsync(cut, "競走馬");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "競走馬情報の収集"));
        var target = cut.FindComponents<FluentTextField>().Single(x => x.Instance.Label?.ToString() == "対象ID");
        await cut.InvokeAsync(() => target.Instance.ValueChanged.InvokeAsync("H002"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Last(x => x.Markup.Contains("収集を依頼</")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => Assert.AreEqual(1, handler.ManualRequests));
        Assert.IsNotNull(handler.LastManualRequest);
        Assert.AreEqual(ResourceType.Horse, handler.LastManualRequest.ResourceType);
        Assert.AreEqual("H002", handler.LastManualRequest.ResourceId);
        Assert.AreEqual("horse-profile", handler.LastManualRequest.DefinitionId);
    }

    [TestMethod]
    public async Task Filters_ArePersistedInUrlAndDetailReturnUrl()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new ResourceHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Jobs>();
        await ClickButtonAsync(cut, "待機中");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "H001"));
        await SelectAsync(cut, "対象種別", "Horse");
        var provider = cut.FindComponents<FluentTextField>()
            .Single(x => x.Instance.Placeholder?.ToString() == "提供元");
        await cut.InvokeAsync(() => provider.Instance.ValueChanged.InvokeAsync("JRA"));
        await ClickFluentButtonAsync(cut, "絞り込む");

        StringAssert.Contains(context.Services.GetRequiredService<NavigationManager>().Uri, "view=waiting");
        StringAssert.Contains(context.Services.GetRequiredService<NavigationManager>().Uri, "type=Horse");
        var link = cut.FindAll("a").Single(x => x.TextContent.Contains("H001"));
        StringAssert.Contains(link.GetAttribute("href"), "returnUrl=");
    }

    private static Task ClickFluentButtonAsync(IRenderedComponent<Jobs> cut, string text) => cut.InvokeAsync(() =>
        cut.FindComponents<FluentButton>().First(x => x.Markup.Contains($">{text}</")).Instance.OnClick.InvokeAsync());

    private static Task ClickButtonAsync(IRenderedComponent<Jobs> cut, string text) => cut.InvokeAsync(() =>
        cut.FindAll("button").First(x => x.TextContent.Trim().StartsWith(text, StringComparison.Ordinal)).Click());

    private static Task SelectAsync(IRenderedComponent<Jobs> cut, string label, string value) => cut.InvokeAsync(() =>
        cut.Find($"select[aria-label='{label}']").Change(value));

    private sealed class ResourceHandler : HttpMessageHandler
    {
        private static readonly ResourceKey Resource = new(ResourceType.Horse, "jra", "H001");
        private static readonly CollectionDefinitionId Definition = new("horse-profile");
        public int ManualRequests { get; private set; }
        public int Previews { get; private set; }
        public CreateCollectionRequest? LastManualRequest { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/requests/bulk/preview"))
            {
                Previews++;
                return await Ok(new CollectionBulkPreview(Definition, 1, 1, [Resource]));
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/requests"))
            {
                ManualRequests++;
                LastManualRequest = await request.Content!.ReadFromJsonAsync<CreateCollectionRequest>(cancellationToken);
                return await Ok(new CollectionRequestReceipt(Guid.NewGuid(), Guid.NewGuid(), true));
            }
            object value = request.RequestUri!.AbsolutePath switch
            {
                "/api/admin/collection/tasks" => new[] { new CollectionTaskSummary(Guid.NewGuid(), Resource,
                    Definition, CollectionTaskStatus.Pending, CollectionLane.Normal, 50, 1,
                    DateTimeOffset.UtcNow, 0) },
                "/api/admin/collection/tasks/search" => new CollectionTaskPage(1, 1, 50,
                    [new CollectionTaskSummary(Guid.NewGuid(), Resource, Definition, CollectionTaskStatus.Pending,
                        CollectionLane.Normal, 50, 1, DateTimeOffset.UtcNow, 0)]),
                "/api/admin/collection/states/search" => new CollectionStatePage(1, 1, 50,
                    [new CollectionStateSnapshot(Resource, Definition, 1, 1, DateTimeOffset.UtcNow, null,
                        CollectionStateStatus.Current)]),
                "/api/admin/collection/progress" => new CollectionProgressSnapshot(
                    new Dictionary<ResourceType, int> { [ResourceType.Horse] = 1 },
                    new Dictionary<CollectionStateStatus, int> { [CollectionStateStatus.Pending] = 1 },
                    new Dictionary<CollectionLane, int>(), new Dictionary<int, int>(),
                    new Dictionary<string, int>(), 0),
                "/api/admin/collection/pipeline" => new HorseRacingPrediction.CollectionOperations.CollectionPlatform.CollectionPipelineState(false, null, DateTimeOffset.UtcNow),
                "/api/admin/collection/failure-notifications" => Array.Empty<PendingCollectionFailureNotification>(),
                "/api/admin/collection/backfills" => Array.Empty<BackfillBatchSnapshot>(),
                _ when request.RequestUri.AbsolutePath.StartsWith("/api/admin/collection/resources/") =>
                    new CollectionResourceDetail(new(Resource, Definition, 0, 1, null, null,
                        CollectionStateStatus.Pending), [], [], [], []),
                _ => Array.Empty<object>(),
            };
            return await Ok(value);
        }

        private static Task<HttpResponseMessage> Ok(object value) => Task.FromResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = JsonContent.Create(value) });
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new HttpRequestException("offline");
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
