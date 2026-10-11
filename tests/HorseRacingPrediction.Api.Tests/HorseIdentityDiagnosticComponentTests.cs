using Bunit;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class HorseIdentityDiagnosticComponentTests
{
    private const string HorseId = "horse-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20";

    [TestMethod]
    public async Task DiagnosticPage_RendersRepeatedEvidenceAndRefreshesWithSameCanonicalPath()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new DiagnosticHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<HorseIdentityDiagnostics>(parameters => parameters.Add(x => x.HorseId, HorseId));

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "診断結果"));
        StringAssert.Contains(cut.Markup, "ProfileReadModel");
        StringAssert.Contains(cut.Markup, "初回登録名");
        StringAssert.Contains(cut.Markup, "登録時の名前");
        StringAssert.Contains(cut.Markup, "登録時正規化名");
        StringAssert.Contains(cut.Markup, "現在の診断対象");
        StringAssert.Contains(cut.Markup, "現行正規名");
        StringAssert.Contains(cut.Markup, "2020-04-03");
        StringAssert.Contains(cut.Markup, "2022-05-01");
        StringAssert.Contains(cut.Markup, "Invalid");
        StringAssert.Contains(cut.Markup, "2026-10-11 01:00:00 UTC");
        StringAssert.Contains(cut.Markup, "2026-10-11 01:01:00 UTC");
        StringAssert.Contains(cut.Markup, "2026-10-11 01:02:00 UTC");
        StringAssert.Contains(cut.Markup, "2026-10-11 01:03:00 UTC");
        StringAssert.Contains(cut.Markup, "source URLの状態");
        StringAssert.Contains(cut.Markup, "保存先ごとの読取時点は異なります");
        Assert.IsTrue(CountOccurrences(cut.Markup, "jra-horse:source-1") >= 4,
            "Both exactly-equal repeated source rows must remain visible in the desktop and narrow render paths.");
        StringAssert.Contains(cut.Markup, "診断結果");
        Assert.AreEqual($"/api/v2/admin/identity-diagnostics/horses/{HorseId}", handler.Requests.Single());

        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("再取得", StringComparison.Ordinal)).Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => Assert.HasCount(2, handler.Requests));
    }

    [TestMethod]
    public async Task DiagnosticPage_InvalidInputShowsInlineErrorWithoutRequest()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new DiagnosticHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityDiagnostics>();

        var input = cut.FindComponents<FluentTextField>().Single();
        await cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync("horse-NOT-CANONICAL"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains(">診断<", StringComparison.Ordinal)).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "小文字のGUID"));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task DiagnosticPage_ImmediateFieldChangeIsUsedByDiagnoseAction()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new DiagnosticHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityDiagnostics>();

        var input = cut.FindComponents<FluentTextField>().Single();
        Assert.IsTrue(input.Instance.Immediate);
        StringAssert.Contains(cut.Markup, "horse-で始まる小文字のGUIDを入力してください。");
        await cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(HorseId));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains(">診断<", StringComparison.Ordinal)).Instance.OnClick.InvokeAsync());

        var navigation = context.Services.GetRequiredService<NavigationManager>();
        Assert.AreEqual($"settings/identity-diagnostics/horses/{HorseId}",
            navigation.ToBaseRelativePath(navigation.Uri));
        Assert.IsEmpty(cut.FindAll(".field-error"));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task DiagnosticPage_WrapsDesktopGridsAndRetainsMobileEvidenceCards()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new DiagnosticHandler()) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityDiagnostics>(parameters => parameters.Add(x => x.HorseId, HorseId));

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "診断結果"));
        Assert.AreEqual(2, cut.FindAll(".diagnostic-grid").Count);
        Assert.AreEqual(2, cut.FindAll(".diagnostic-cards").Count);
        Assert.AreEqual(4, cut.FindAll(".diagnostic-card").Count);
    }

    [TestMethod]
    public async Task DiagnosticPage_DistinguishesMissingAndEmptyInitialNormalizedName()
    {
        foreach (var (value, expected) in new (string? Value, string Expected)[]
        {
            (null, "（欠落）"),
            (string.Empty, "（空文字）")
        })
        {
            var (app, original) = await TestApplicationFactory.CreateAsync();
            await using var application = app;
            using var ignored = original;
            var handler = new DiagnosticHandler { InitialNormalizedName = value };
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            await using var context = CreateContext(app.Services, http);
            var cut = context.Render<HorseIdentityDiagnostics>(parameters => parameters.Add(x => x.HorseId, HorseId));

            cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, expected));
        }
    }

    [TestMethod]
    public async Task DiagnosticPage_ShowsPendingAndRefreshStateWithoutLosingOldEvidence()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new DiagnosticHandler
        {
            PendingResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityDiagnostics>(parameters => parameters.Add(x => x.HorseId, HorseId));
        var initialGate = handler.PendingResponse!;
        try
        {
            cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "診断を読み込んでいます"));
            Assert.IsTrue(cut.FindComponents<FluentButton>().All(x => x.Instance.Disabled));
        }
        finally
        {
            initialGate.TrySetResult(DiagnosticHandler.CreateSuccessResponse());
        }
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "2026-10-11 01:00:00 UTC"));

        var refreshGate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.PendingResponse = refreshGate;
        var refreshTask = cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("再取得", StringComparison.Ordinal)).Instance.OnClick.InvokeAsync());
        try
        {
            cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "再取得中です"));
            StringAssert.Contains(cut.Markup, "2026-10-11 01:03:00 UTC");
            Assert.IsTrue(cut.FindComponents<FluentButton>().All(x => x.Instance.Disabled));
            Assert.AreEqual(2, handler.Requests.Count);
        }
        finally
        {
            refreshGate.TrySetResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"detail\":\"safe\"}", Encoding.UTF8, "application/problem+json")
            });
            await refreshTask;
        }
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "時間をおいて再取得"));
        StringAssert.Contains(cut.Markup, "2026-10-11 01:00:00 UTC");
        StringAssert.Contains(cut.Markup, "2026-10-11 01:03:00 UTC");
    }

    [TestMethod]
    public async Task DiagnosticPage_DistinguishesUnsupportedMissingTruncatedAndEmptyEvidence()
    {
        var cases = new (Action<JsonObject> Mutate, string[] Expected)[]
        {
            (diagnostic =>
            {
                diagnostic["registrationStatus"] = "Unsupported";
                diagnostic["evidenceBlockers"] = new JsonArray("UnsupportedEventVersion");
            }, ["Unsupported", "UnsupportedEventVersion"]),
            (diagnostic =>
            {
                diagnostic["currentRecordStatus"] = "Missing";
                diagnostic["sources"] = new JsonArray();
                diagnostic["comparisons"] = new JsonArray();
                diagnostic["evidenceBlockers"] = new JsonArray("SourceEvidenceMissing");
            }, ["Missing", "保存された証拠はありません", "比較できる証拠がありません", "SourceEvidenceMissing"]),
            (diagnostic =>
            {
                diagnostic["sourcesTruncated"] = true;
                diagnostic["evidenceBlockers"] = new JsonArray("SourcesTruncated");
            }, ["証拠は上限で切り詰められています", "SourcesTruncated"]),
            (diagnostic =>
            {
                diagnostic["comparisons"] = new JsonArray();
                diagnostic["evidenceBlockers"] = new JsonArray("ComparisonEvidenceMissing");
            }, ["比較できる証拠がありません", "ComparisonEvidenceMissing"])
        };

        foreach (var (mutate, expected) in cases)
        {
            var (app, original) = await TestApplicationFactory.CreateAsync();
            await using var application = app;
            using var ignored = original;
            var handler = new DiagnosticHandler
            {
                SuccessJsonTransform = json =>
                {
                    var root = JsonNode.Parse(json)!.AsObject();
                    mutate(root["diagnostic"]!.AsObject());
                    return root.ToJsonString();
                }
            };
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            await using var context = CreateContext(app.Services, http);
            var cut = context.Render<HorseIdentityDiagnostics>(parameters => parameters.Add(x => x.HorseId, HorseId));

            cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "診断結果"));
            Assert.IsFalse(cut.Markup.Contains("指定した馬が見つかりません", StringComparison.Ordinal));
            foreach (var text in expected)
                StringAssert.Contains(cut.Markup, text);
        }
    }

    [TestMethod]
    public async Task DiagnosticPage_NotFoundAndUnavailableShowDistinctRetryableStates()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new DiagnosticHandler(HttpStatusCode.NotFound);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var notFound = context.Render<HorseIdentityDiagnostics>(parameters => parameters.Add(x => x.HorseId, HorseId));
        notFound.WaitForAssertion(() => StringAssert.Contains(notFound.Markup, "指定した馬が見つかりません"));
        Assert.IsFalse(notFound.Markup.Contains("System.", StringComparison.Ordinal));

        var (secondApp, secondOriginal) = await TestApplicationFactory.CreateAsync();
        await using var secondApplication = secondApp;
        using var secondIgnored = secondOriginal;
        var unavailableHandler = new DiagnosticHandler(HttpStatusCode.ServiceUnavailable);
        using var unavailableHttp = new HttpClient(unavailableHandler) { BaseAddress = new Uri("http://localhost") };
        await using var secondContext = CreateContext(secondApp.Services, unavailableHttp);
        var unavailable = secondContext.Render<HorseIdentityDiagnostics>(parameters => parameters.Add(x => x.HorseId, HorseId));
        unavailable.WaitForAssertion(() => StringAssert.Contains(unavailable.Markup, "保存先を利用できません"));
        StringAssert.Contains(unavailable.Markup, "再取得");
    }

    [TestMethod]
    public async Task DiagnosticPage_ServerBadRequestIsShownInline()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new DiagnosticHandler(HttpStatusCode.BadRequest);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityDiagnostics>(parameters => parameters.Add(x => x.HorseId, HorseId));

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Find(".field-error").TextContent, "HorseIdを確認"));
    }

    [TestMethod]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    public async Task DiagnosticPage_RefreshFailureRetainsPreviousResultAndInput(HttpStatusCode failureStatus)
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new DiagnosticHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityDiagnostics>(parameters => parameters.Add(x => x.HorseId, HorseId));
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "診断対象"));

        handler.Status = failureStatus;
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("再取得", StringComparison.Ordinal)).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "診断対象"));
        StringAssert.Contains(cut.Markup, HorseId);
        StringAssert.Contains(cut.Markup, "登録時の名前");
        StringAssert.Contains(cut.Markup, "現在の診断対象");
        StringAssert.Contains(cut.Markup, "2026-10-11 01:00:00 UTC");
        StringAssert.Contains(cut.Markup, "2026-10-11 01:03:00 UTC");
        StringAssert.Contains(cut.Markup, "再取得してください");
        Assert.AreEqual(2, handler.Requests.Count);
    }

    private static int CountOccurrences(string value, string term)
        => value.Split(term, StringSplitOptions.None).Length - 1;

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

    private sealed class DiagnosticHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = status;
        public string? InitialNormalizedName { get; set; } = "登録時正規化名";
        public TaskCompletionSource<HttpResponseMessage>? PendingResponse { get; set; }
        public Func<string, string>? SuccessJsonTransform { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri?.PathAndQuery ?? string.Empty);
            if (PendingResponse is { } pending)
                return await pending.Task.WaitAsync(cancellationToken);
            if (Status != HttpStatusCode.OK)
                return new HttpResponseMessage(Status) { Content = new StringContent("{\"detail\":\"safe\"}", Encoding.UTF8, "application/problem+json") };

            var json = """
                {"diagnostic":{"horseId":"horse-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20","eventStoreObservedFromUtc":"2026-10-11T01:00:00Z","eventStoreObservedToUtc":"2026-10-11T01:01:00Z","collectionStoreObservedFromUtc":"2026-10-11T01:02:00Z","collectionStoreObservedToUtc":"2026-10-11T01:03:00Z","currentRecordStatus":"Supported","currentRegisteredName":"現在の診断対象","currentNormalizedName":"現行正規名","currentBirthDate":"2022-05-01","registrationStatus":"Supported","initialRegisteredName":"登録時の名前","initialNormalizedName":"登録時正規化名","initialBirthDate":"2020-04-03","sources":[{"origin":"ProfileReadModel","status":"Supported","sourceIdentity":"jra-horse:source-1","normalizedSourceIdentity":"jra-horse:source-1","sourceUrlStatus":"Invalid","sourceUrlHorseIdentity":null},{"origin":"ProfileReadModel","status":"Supported","sourceIdentity":"jra-horse:source-1","normalizedSourceIdentity":"jra-horse:source-1","sourceUrlStatus":"Invalid","sourceUrlHorseIdentity":null}],"comparisons":[{"rule":"SourceIdentity","inputField":"sourceIdentity","evidenceStatus":"Supported","candidateHorseId":"horse-source-1","matchStatus":"Matched"},{"rule":"SourceIdentity","inputField":"sourceIdentity","evidenceStatus":"Supported","candidateHorseId":"horse-source-1","matchStatus":"Matched"}],"evidenceBlockers":["確認は読み取り専用です。"],"sourcesTruncated":false,"coverage":"PointInTime"}}
                """;
            json = json.Replace("\"initialNormalizedName\":\"登録時正規化名\"",
                $"\"initialNormalizedName\":{JsonSerializer.Serialize(InitialNormalizedName)}", StringComparison.Ordinal);
            if (SuccessJsonTransform is { } transform)
                json = transform(json);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        public static HttpResponseMessage CreateSuccessResponse() => new(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"diagnostic":{"horseId":"horse-4d2f2a58-0e31-45d6-bf9c-9e6f3b5a7f20","eventStoreObservedFromUtc":"2026-10-11T01:00:00Z","eventStoreObservedToUtc":"2026-10-11T01:01:00Z","collectionStoreObservedFromUtc":"2026-10-11T01:02:00Z","collectionStoreObservedToUtc":"2026-10-11T01:03:00Z","currentRecordStatus":"Supported","currentRegisteredName":"現在の診断対象","currentNormalizedName":"現行正規名","currentBirthDate":"2022-05-01","registrationStatus":"Supported","initialRegisteredName":"登録時の名前","initialNormalizedName":"登録時正規化名","initialBirthDate":"2020-04-03","sources":[],"comparisons":[],"evidenceBlockers":[],"sourcesTruncated":false,"coverage":"PointInTime"}}
                """, Encoding.UTF8, "application/json")
        };
    }
}
