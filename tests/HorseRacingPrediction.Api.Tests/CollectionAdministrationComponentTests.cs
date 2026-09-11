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

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "収集対象はまだ登録されていません"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("まとめて再取得</")).Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => Assert.IsTrue(cut.FindComponents<FluentButton>()
            .Last(x => x.Markup.Contains("再取得を依頼</")).Instance.Disabled));
        StringAssert.Contains(cut.Markup, "確認後に対象が増えることはありません");
    }

    [TestMethod]
    public async Task ResourceSelection_ShowsStateAndManualAction()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new ResourceHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Jobs>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "H001"));
        StringAssert.Contains(cut.Markup, "競走馬プロフィール");
        StringAssert.Contains(cut.Markup, "処理待ち");
        StringAssert.Contains(cut.Markup, "収集対象");
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("H001</")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "この対象を再取得"));
        StringAssert.Contains(cut.Markup, "保存済みの取得先候補はありません");
        StringAssert.Contains(cut.Markup, "実行履歴はまだありません");
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .First(x => x.Markup.Contains("再取得を依頼</")).Instance.OnClick.InvokeAsync());
        Assert.AreEqual(1, handler.ManualRequests);
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("まとめて再取得</")).Instance.OnClick.InvokeAsync());
        var ids = cut.FindComponents<FluentTextField>()
            .Single(x => x.Instance.Label?.ToString()?.StartsWith("対象ID") == true);
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
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "H001"));
        var search = cut.FindComponents<FluentTextField>()
            .Single(x => x.Instance.Placeholder?.ToString()?.Contains("対象ID") == true);
        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("騎手"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains(">検索</")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "検索条件に一致する収集対象はありません"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("検索条件をクリア</")).Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "H001"));
    }

    private sealed class ResourceHandler : HttpMessageHandler
    {
        private static readonly ResourceKey Resource = new(ResourceType.Horse, "jra", "H001");
        private static readonly CollectionDefinitionId Definition = new("horse-profile");
        public int ManualRequests { get; private set; }
        public int Previews { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/requests/bulk/preview"))
            {
                Previews++;
                return Ok(new CollectionBulkPreview(Definition, 1, 1, [Resource]));
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/requests"))
            {
                ManualRequests++;
                return Ok(new CollectionRequestReceipt(Guid.NewGuid(), Guid.NewGuid(), true));
            }
            object value = request.RequestUri!.AbsolutePath switch
            {
                "/api/admin/collection/tasks" => new[] { new CollectionTaskSummary(Guid.NewGuid(), Resource,
                    Definition, CollectionTaskStatus.Pending, CollectionLane.Normal, 50, 1,
                    DateTimeOffset.UtcNow, 0) },
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
            return Ok(value);
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
