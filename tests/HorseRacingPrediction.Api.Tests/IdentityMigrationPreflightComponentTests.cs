using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Pages;
using HorseRacingPrediction.Contracts.Identity;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class IdentityMigrationPreflightComponentTests
{
    private const string HorseId = "horse-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20";
    private const string OpaqueCursor = "protected-cursor-do-not-render";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task Page_DoesNotFetchUntilExplicitStartAndShowsObservationUnitsWithoutCursor()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new PreflightHandler(Page(IdentityMigrationPreflightPhase.HorseRoots, [Evidence()], OpaqueCursor));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<IdentityMigrationPreflight>();

        StringAssert.Contains(cut.Markup, "同時点のスナップショットではありません");
        Assert.IsEmpty(handler.Requests);
        await ClickAsync(cut, "新しい調査を開始");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, HorseId));
        Assert.AreEqual(1, handler.Requests.Count);
        StringAssert.Contains(cut.Markup, "イベント保存先");
        StringAssert.Contains(cut.Markup, "既知オブジェクト数にはテーブルとインデックスを含みます。テーブル数や保存行数の全件調査ではありません。");
        StringAssert.Contains(cut.Markup, "スキーマのみ確認（行は未調査）");
        StringAssert.Contains(cut.Markup, "この調査ページ内の観測状態");
        StringAssert.Contains(cut.Markup, "上限付きの馬IDソース観測");
        StringAssert.Contains(cut.Markup, "予測スケジュールとローカル収集キューは行を調べず");
        StringAssert.Contains(cut.Markup, "関連レース");
        Assert.IsFalse(cut.Markup.Contains(OpaqueCursor, StringComparison.Ordinal));
        Assert.IsFalse(cut.Markup.Contains("移行可能と判定", StringComparison.Ordinal));
        Assert.AreEqual(2, cut.FindAll(".preflight-grid-scroll").Count);
        Assert.AreEqual(2, cut.FindAll(".preflight-cards").Count);
        var desktopEvidence = cut.FindAll(".preflight-grid-scroll")[1].TextContent;
        StringAssert.Contains(desktopEvidence, "初回正規化名");
        StringAssert.Contains(desktopEvidence, "初回生年月日");
        StringAssert.Contains(desktopEvidence, "現在正規化名");
        StringAssert.Contains(desktopEvidence, "race-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20 #7");
    }

    [TestMethod]
    public async Task Page_ReadNextReplacesCurrentPageAndKeepsRepeatedPhaseRows()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var duplicate = Evidence();
        var handler = new PreflightHandler(
            Page(IdentityMigrationPreflightPhase.HorseRoots, [duplicate, duplicate], OpaqueCursor),
            Page(IdentityMigrationPreflightPhase.HorseEvents, [duplicate with { Phase = IdentityMigrationPreflightPhase.HorseEvents }], null));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<IdentityMigrationPreflight>();

        await ClickAsync(cut, "新しい調査を開始");
        cut.WaitForAssertion(() => Assert.AreEqual(2, cut.FindAll(".evidence-card").Count));
        await ClickAsync(cut, "次のページを読む");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "馬イベント"));
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsFalse(cut.Markup.Contains("馬IDの登録情報", StringComparison.Ordinal));
        Assert.IsFalse(cut.Markup.Contains(OpaqueCursor, StringComparison.Ordinal));
    }

    [DataRow(HttpStatusCode.TooManyRequests)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    [TestMethod]
    public async Task Page_TransientNextFailureKeepsOldEvidenceAndRetriesOnlyCurrentPage(HttpStatusCode status)
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new PreflightHandler(Page(IdentityMigrationPreflightPhase.HorseRoots, [Evidence()], OpaqueCursor))
        {
            FailureStatus = status,
            FailOnRequest = 2,
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<IdentityMigrationPreflight>();
        await ClickAsync(cut, "新しい調査を開始");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, HorseId));

        await ClickAsync(cut, "次のページを読む");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "このページを読み取れませんでした"));
        StringAssert.Contains(cut.Markup, "表示中の結果は前回の観測です。新しいページの読み取りは完了していません。");
        StringAssert.Contains(cut.Markup, HorseId);
        Assert.IsFalse(cut.Markup.Contains(OpaqueCursor, StringComparison.Ordinal));
        await ClickAsync(cut, "現在のページを再読込");

        Assert.AreEqual(3, handler.Requests.Count);
        Assert.AreEqual($"?cursor={Uri.EscapeDataString(OpaqueCursor)}", handler.Requests[1]);
        Assert.AreEqual(handler.Requests[1], handler.Requests[2]);
    }

    [DataRow(HttpStatusCode.NoContent)]
    [DataRow(HttpStatusCode.NotFound)]
    [TestMethod]
    public async Task Page_NullContinuationKeepsStaleEvidenceAndCanRetryCurrentCursor(HttpStatusCode status)
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new PreflightHandler(
            Page(IdentityMigrationPreflightPhase.HorseRoots, [Evidence()], OpaqueCursor),
            Page(IdentityMigrationPreflightPhase.HorseEvents, [Evidence() with { Phase = IdentityMigrationPreflightPhase.HorseEvents }], null))
        {
            NullOnRequest = 2,
            NullStatus = status,
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<IdentityMigrationPreflight>();
        await ClickAsync(cut, "新しい調査を開始");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, HorseId));

        await ClickAsync(cut, "次のページを読む");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "続きの結果が返されませんでした"));
        StringAssert.Contains(cut.Markup, "表示中の結果は前回の観測です。");
        StringAssert.Contains(cut.Markup, HorseId);
        Assert.IsFalse(cut.Markup.Contains(OpaqueCursor, StringComparison.Ordinal));
        await ClickAsync(cut, "現在のページを再読込");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "馬イベント"));
        Assert.AreEqual(3, handler.Requests.Count);
        Assert.AreEqual(handler.Requests[1], handler.Requests[2]);
    }

    [TestMethod]
    public async Task Page_BadRequestDiscardsStalePageAndRequiresFreshScan()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new PreflightHandler(Page(IdentityMigrationPreflightPhase.HorseRoots, [Evidence()], OpaqueCursor))
        {
            FailureStatus = HttpStatusCode.BadRequest,
            FailOnRequest = 2,
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<IdentityMigrationPreflight>();
        await ClickAsync(cut, "新しい調査を開始");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, HorseId));

        await ClickAsync(cut, "次のページを読む");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "続きの読み取りを続けられません"));
        Assert.IsFalse(cut.Markup.Contains(HorseId, StringComparison.Ordinal));
        Assert.IsFalse(cut.Markup.Contains(OpaqueCursor, StringComparison.Ordinal));
        Assert.IsTrue(cut.FindComponents<FluentButton>().Any(button => button.Markup.Contains("新しい調査を開始", StringComparison.Ordinal)));
        Assert.IsFalse(cut.Markup.Contains("現在のページを再読込", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Page_PendingRequestDisablesActionsAndRetainsVisiblePage()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PreflightHandler(Page(IdentityMigrationPreflightPhase.HorseRoots, [Evidence()], OpaqueCursor))
        {
            PendingResponse = gate,
            PendingOnRequest = 2,
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<IdentityMigrationPreflight>();
        await ClickAsync(cut, "新しい調査を開始");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, HorseId));

        var nextTask = ClickAsync(cut, "次のページを読む");
        try
        {
            cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "前回の観測です"));
            StringAssert.Contains(cut.Markup, HorseId);
            Assert.IsTrue(cut.FindComponents<FluentButton>().All(button => button.Instance.Disabled));
            Assert.AreEqual(2, handler.Requests.Count);
        }
        finally
        {
            gate.TrySetResult(PreflightHandler.SuccessResponse(Page(IdentityMigrationPreflightPhase.HorseEvents, [Evidence()], null)));
            await nextTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "馬イベント"));
    }

    private static async Task ClickAsync(IRenderedComponent<IdentityMigrationPreflight> cut, string label)
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

    private static IdentityMigrationPreflightPage Page(IdentityMigrationPreflightPhase phase,
        IReadOnlyList<IdentityMigrationPreflightHorseEvidence> evidence, string? nextCursor) => new(
        IdentityMigrationPreflightPageState.InProgress, phase, DateTimeOffset.Parse("2026-10-12T01:02:03Z"),
        evidence.Count, new(1, 1, 0, 1, 0, 0, 0, 1, 1, 0),
        [
            new(IdentityMigrationPreflightSurface.EventStore, IdentityMigrationPreflightObservationStatus.Observed,
                DateTimeOffset.Parse("2026-10-12T01:02:01Z"), 3, 1),
            new(IdentityMigrationPreflightSurface.CollectionPlatform, IdentityMigrationPreflightObservationStatus.SchemaOnly,
                DateTimeOffset.Parse("2026-10-12T01:02:02Z"), 4, 0),
            new(IdentityMigrationPreflightSurface.PredictionSchedule, IdentityMigrationPreflightObservationStatus.NotInspected, null, 0, 0),
            new(IdentityMigrationPreflightSurface.LocalCollectionQueue, IdentityMigrationPreflightObservationStatus.NotConfigured, null, 0, 0),
        ], evidence, nextCursor);

    private static IdentityMigrationPreflightHorseEvidence Evidence() => new(
        HorseId, IdentityMigrationPreflightPhase.HorseRoots, IdentityMigrationPreflightObservationStatus.Unverifiable,
        "現在の登録名", "現在の正規化名", new DateOnly(2021, 2, 3), "初回登録名", string.Empty, null,
        "jra-horse:2024108210", IdentityMigrationPreflightObservationStatus.Observed,
        [new("race-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20", 7)], 2, true);

    private static int CountOccurrences(string input, string value) => input.Split(value, StringSplitOptions.None).Length - 1;

    private sealed class PreflightHandler(params IdentityMigrationPreflightPage[] pages) : HttpMessageHandler
    {
        private int requestCount;
        public List<string> Requests { get; } = [];
        public int? FailOnRequest { get; init; }
        public HttpStatusCode FailureStatus { get; init; } = HttpStatusCode.OK;
        public int? NullOnRequest { get; init; }
        public HttpStatusCode NullStatus { get; init; } = HttpStatusCode.NoContent;
        public int? PendingOnRequest { get; init; }
        public TaskCompletionSource<HttpResponseMessage>? PendingResponse { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref requestCount);
            Requests.Add(request.RequestUri?.Query ?? string.Empty);
            if (PendingOnRequest == index && PendingResponse is { } pending)
                return await pending.Task.WaitAsync(cancellationToken);
            if (NullOnRequest == index)
                return new HttpResponseMessage(NullStatus);
            if (FailOnRequest == index)
                return new HttpResponseMessage(FailureStatus);
            return index <= pages.Length ? SuccessResponse(pages[index - 1]) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        public static HttpResponseMessage SuccessResponse(IdentityMigrationPreflightPage page) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new IdentityMigrationPreflightResponse(page), options: Json),
        };
    }
}
