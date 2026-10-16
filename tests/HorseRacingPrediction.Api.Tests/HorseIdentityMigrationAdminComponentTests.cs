using System.Net;
using System.Text.Json;
using Bunit;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Pages;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityMigrationAdminComponentTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string OldHorseId = "horse-d198b3e2-771f-5dcb-94dc-a739e98b6119";
    private const string JraIdentity = "jra-horse:2024100828";

    [TestMethod]
    public async Task InitialPageShowsUncheckedHypothesesAndDoesNotCallBackend()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new MigrationHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<HorseIdentityMigrationAdmin>();

        StringAssert.Contains(cut.Markup, "名前だけでは同一性を確認できません");
        StringAssert.Contains(cut.Markup, "ナムラアラバイ → jra-horse:2024100828");
        StringAssert.Contains(cut.Markup, "生年月日とプロフィールの根拠が不足しています。");
        Assert.AreEqual(3, cut.FindAll(".migration-card").Count);
        Assert.IsTrue(cut.FindComponents<FluentCheckbox>().All(item => !item.Instance.Value));
        Assert.IsEmpty(handler.Requests);
        Assert.IsTrue(cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("選択内容をプレビュー", StringComparison.Ordinal)).Instance.Disabled);
    }

    [TestMethod]
    public async Task PreviewThenExplicitConfirmationStagesSameManifestAndShowsManualReceipt()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new MigrationHandler
        {
            Preview = StageablePreview(),
            Staged = new HorseIdentityMigrationOperationResponse(Guid.Parse("c54c11b0-b17a-4aed-a956-b8feb0c57a74"),
                "AwaitingActions", "None", DateTimeOffset.Parse("2026-10-15T00:00:00Z"),
                DateTimeOffset.Parse("2026-10-15T00:00:00Z"), []),
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();
        var checkbox = cut.FindComponents<FluentCheckbox>().First();
        await cut.InvokeAsync(() => checkbox.Instance.ValueChanged.InvokeAsync(true));
        await ClickAsync(cut, "選択内容をプレビュー");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "表示された候補と保存状態を確認してください"));
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        Assert.AreEqual("/api/v2/admin/horse-id-migration-previews", handler.Requests[0].Path);
        var previewManifest = JsonDocument.Parse(handler.Requests[0].Body!).RootElement;
        Assert.AreEqual(OldHorseId, previewManifest.GetProperty("entries")[0].GetProperty("oldHorseId").GetString());
        Assert.AreEqual(JraIdentity, previewManifest.GetProperty("entries")[0].GetProperty("jraIdentity").GetString());
        Assert.IsTrue(previewManifest.GetProperty("entries")[0].GetProperty("hypothesisAccepted").GetBoolean());

        await ClickAsync(cut, "この固定内容を実行依頼として保存");
        StringAssert.Contains(cut.Markup, "実行依頼を保存しますか？");
        Assert.AreEqual(1, handler.Requests.Count, "Opening confirmation must not stage automatically.");
        await ClickAsync(cut, "この内容を実行依頼として保存");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "まだ実行されていません"));
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.AreEqual("/api/v2/admin/horse-id-migration-operations", handler.Requests[1].Path);
        using var stageJson = JsonDocument.Parse(handler.Requests[1].Body!);
        var stage = stageJson.RootElement;
        Assert.AreEqual(OldHorseId, stage.GetProperty("manifest").GetProperty("entries")[0].GetProperty("oldHorseId").GetString());
        Assert.AreEqual(JraIdentity, stage.GetProperty("manifest").GetProperty("entries")[0].GetProperty("jraIdentity").GetString());
        Assert.AreEqual(new string('A', 64), stage.GetProperty("previewFingerprint").GetString());
        Assert.IsFalse(handler.Requests.Any(request => request.Method == HttpMethod.Get), "The stage receipt must not trigger status polling.");
        StringAssert.Contains(cut.Markup, "c54c11b0-b17a-4aed-a956-b8feb0c57a74");
        StringAssert.Contains(cut.Markup, "既存の GitHub Actions の app-deploy workflow を手動 dispatch");
        StringAssert.Contains(cut.Markup, "処理が開始されるまでは AwaitingActions のままです");
    }

    [TestMethod]
    public async Task BlockedPreviewIsVisibleAndCannotBeStaged()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new MigrationHandler
        {
            Preview = new HorseIdentityMigrationPreview(false, null,
                [HorseIdentityMigrationPreviewBlocker.IncompleteEvidence, HorseIdentityMigrationPreviewBlocker.DestinationCollision],
                [], [], [], [], 4, 128),
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();
        await cut.InvokeAsync(() => cut.FindComponents<FluentCheckbox>().First().Instance.ValueChanged.InvokeAsync(true));

        await ClickAsync(cut, "選択内容をプレビュー");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "この内容は保存できません"));
        StringAssert.Contains(cut.Markup, "変換に必要な証拠が不足しています。");
        StringAssert.Contains(cut.Markup, "変換先に既存データとの競合があります。");
        Assert.IsTrue(cut.FindComponents<FluentButton>()
            .Single(button => button.Markup.Contains("この固定内容を実行依頼として保存", StringComparison.Ordinal))
            .Instance.Disabled);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task QueryPrefillsManualStatusLookupWithoutInitialFetch()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var operationId = Guid.Parse("c54c11b0-b17a-4aed-a956-b8feb0c57a74");
        var handler = new MigrationHandler
        {
            Status = new HorseIdentityMigrationOperationResponse(operationId, "Partial", "UnverifiedPages",
                DateTimeOffset.Parse("2026-10-15T00:00:00Z"), DateTimeOffset.Parse("2026-10-15T00:01:00Z"),
                [new("PageMismatch", "EventStore", 1)]),
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"http://localhost/settings/horse-id-migration?operationId={operationId:D}");
        var cut = context.Render<HorseIdentityMigrationAdmin>();

        Assert.IsEmpty(handler.Requests, "The query string prefills the field but does not fetch status automatically.");
        StringAssert.Contains(cut.Markup, operationId.ToString("D"));
        await ClickAsync(cut, "状態を更新");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "未検証の一部適用"));
        StringAssert.Contains(cut.Markup, "この画面から再実行・再開はできません");
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
        Assert.AreEqual($"/api/v2/admin/horse-id-migration-operations/{operationId:D}", handler.Requests[0].Path);
    }

    [TestMethod]
    public async Task StaleStageKeepsSelectionAndExplainsThatPreviewMustBeRefreshed()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new MigrationHandler
        {
            Preview = StageablePreview(),
            StageStatus = HttpStatusCode.Conflict,
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();
        await cut.InvokeAsync(() => cut.FindComponents<FluentCheckbox>().First().Instance.ValueChanged.InvokeAsync(true));
        await ClickAsync(cut, "選択内容をプレビュー");
        await ClickAsync(cut, "この固定内容を実行依頼として保存");
        await ClickAsync(cut, "この内容を実行依頼として保存");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "最新の状態をもう一度プレビューしてください"));
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(cut.FindComponents<FluentCheckbox>().First().Instance.Value,
            "A stale preview must not clear the operator's explicit selection.");
        Assert.IsFalse(cut.Markup.Contains("実行依頼を保存しました", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PendingPreviewDisablesDuplicateActionUntilTheRequestSettles()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var gate = new TaskCompletionSource<HorseIdentityMigrationPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new MigrationHandler { PreviewPending = gate };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();
        await cut.InvokeAsync(() => cut.FindComponents<FluentCheckbox>().First().Instance.ValueChanged.InvokeAsync(true));

        var previewTask = ClickAsync(cut, "選択内容をプレビュー");
        try
        {
            cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "読み取り専用プレビューを確認しています"));
            Assert.IsTrue(cut.FindComponents<FluentButton>()
                .Single(button => button.Markup.Contains("プレビュー中", StringComparison.Ordinal)).Instance.Disabled);
            Assert.IsTrue(cut.FindComponents<FluentCheckbox>().All(checkbox => checkbox.Instance.Disabled));
            Assert.AreEqual(1, handler.Requests.Count);
        }
        finally
        {
            gate.TrySetResult(StageablePreview());
            await previewTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "表示された候補と保存状態を確認してください"));
        Assert.AreEqual(1, handler.Requests.Count);
    }

    private static async Task ClickAsync(IRenderedComponent<HorseIdentityMigrationAdmin> cut, string label)
    {
        var button = cut.FindComponents<FluentButton>()
            .Single(component => component.Markup.Contains(label, StringComparison.Ordinal));
        await cut.InvokeAsync(() => button.Instance.OnClick.InvokeAsync());
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

    private static HorseIdentityMigrationPreview StageablePreview() => new(true, new string('A', 64), [],
        [new(OldHorseId, JraIdentity, "horse-4f4ef3a7-9c3c-4878-b7d4-1689b1d2cced")],
        [new("EventStore", "Ready", 3, 0, new string('B', 64))],
        [new(OldHorseId, "horse-4f4ef3a7-9c3c-4878-b7d4-1689b1d2cced", "Ready", false)],
        [new("EventStore", "Horses", 1, new string('C', 64), new string('D', 64))], 10, 256);

    private sealed class MigrationHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];
        public HorseIdentityMigrationPreview? Preview { get; init; }
        public HorseIdentityMigrationOperationResponse? Staged { get; init; }
        public HorseIdentityMigrationOperationResponse? Status { get; init; }
        public HttpStatusCode? StageStatus { get; init; }
        public TaskCompletionSource<HorseIdentityMigrationPreview>? PreviewPending { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new(request.Method, path, body));
            if (path == "/api/v2/admin/horse-id-migration-operations" && StageStatus is { } stageStatus)
                return new HttpResponseMessage(stageStatus)
                {
                    Content = new StringContent("{\"code\":\"PreviewStale\"}", System.Text.Encoding.UTF8, "application/json"),
                };

            object? response = path switch
            {
                "/api/v2/admin/horse-id-migration-previews" => PreviewPending is { } pending
                    ? await pending.Task.WaitAsync(cancellationToken) : Preview,
                "/api/v2/admin/horse-id-migration-operations" => Staged,
                _ when path.StartsWith("/api/v2/admin/horse-id-migration-operations/", StringComparison.Ordinal) => Status,
                _ => null,
            };
            return response is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(response, Json), System.Text.Encoding.UTF8, "application/json"),
                };
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string Path, string? Body);
}
