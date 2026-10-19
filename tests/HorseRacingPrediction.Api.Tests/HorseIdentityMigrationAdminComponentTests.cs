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
        Assert.AreEqual(3, cut.FindAll(".migration-cards[aria-label='馬ID移行の仮説候補'] .migration-card").Count);
        Assert.IsTrue(cut.FindComponents<FluentCheckbox>().All(item => !item.Instance.Value));
        Assert.IsEmpty(handler.Requests);
        Assert.IsTrue(cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("選択内容をプレビュー", StringComparison.Ordinal)).Instance.Disabled);
    }

    [TestMethod]
    public async Task SchemaDiagnosticsAreManualBoundedAndIncompleteNeverEnablesStage()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new MigrationHandler
        {
            SchemaDiagnostics = new HorseIdentityMigrationSchemaDiagnostics(DateTimeOffset.Parse("2026-10-17T01:02:03Z"),
                false,
                [new(HorseIdentityMigrationSchemaSurface.CollectionPlatform, HorseIdentityMigrationSchemaStatus.UnknownShape,
                    26, 0, new string('a', 64), [9],
                    [new(HorseIdentityMigrationSchemaDifferenceKind.ColumnShape, HorseIdentityMigrationSchemaObjectKind.Table,
                        "CollectionTaskRows.Payload", new(DeclaredType: HorseIdentityMigrationSchemaDeclaredType.Text, Nullable: false),
                        new(DeclaredType: HorseIdentityMigrationSchemaDeclaredType.Blob, Nullable: true))])]),
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();

        Assert.IsEmpty(handler.Requests, "Schema diagnostics must not load automatically.");
        await ClickAsync(cut, "保存先を診断");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "診断が未完了です"));
        StringAssert.Contains(cut.Markup, "CollectionTaskRows.Payload");
        StringAssert.Contains(cut.Markup, "期待値");
        StringAssert.Contains(cut.Markup, "観測値");
        StringAssert.Contains(cut.Markup, "バイナリ");
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
        Assert.AreEqual("/api/v2/admin/horse-id-migration-schema-diagnostics", handler.Requests[0].Path);
        Assert.IsFalse(handler.Requests.Any(request => request.Method == HttpMethod.Post));
        Assert.IsTrue(cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("選択内容をプレビュー", StringComparison.Ordinal)).Instance.Disabled);
        Assert.IsFalse(cut.Markup.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SchemaDiagnosticsShowLoadingAndRetainTheLastReportWhenRefreshFails()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var gate = new TaskCompletionSource<HorseIdentityMigrationSchemaDiagnostics>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new MigrationHandler { SchemaDiagnosticsPending = gate };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();

        var loadTask = ClickAsync(cut, "保存先を診断");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "保存先の診断を読み込んでいます"));
        Assert.IsTrue(cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("診断中", StringComparison.Ordinal)).Instance.Disabled);
        gate.TrySetResult(new HorseIdentityMigrationSchemaDiagnostics(DateTimeOffset.Parse("2026-10-17T01:02:03Z"), true,
            [new(HorseIdentityMigrationSchemaSurface.CollectionPlatform, HorseIdentityMigrationSchemaStatus.Observed,
                26, 0, new string('a', 64), [9], [])]));
        await loadTask;
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "診断の読み取りは完了しました"));

        handler.SchemaDiagnosticsStatus = HttpStatusCode.ServiceUnavailable;
        await ClickAsync(cut, "保存先を診断");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "設定された保存先を診断できませんでした"));
        StringAssert.Contains(cut.Markup, "26");
        StringAssert.Contains(cut.Markup, "履歴版");
        Assert.AreEqual(2, handler.Requests.Count(request => request.Path == "/api/v2/admin/horse-id-migration-schema-diagnostics"));
        Assert.IsEmpty(handler.Requests.Where(request => request.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task CandidateSearchShowsLoadingAndPreservesSearchTextOnFailure()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var gate = new TaskCompletionSource<IReadOnlyList<HorseRacingPrediction.Contracts.Horses.HorseSummaryDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new MigrationHandler { SearchHorsesPending = gate };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();
        var search = cut.FindComponents<FluentTextField>().Single(field => field.Instance.Label == "登録馬名またはIDで検索");
        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("保持する検索語"));

        var searchTask = ClickAsync(cut, "登録馬を検索");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "登録馬を検索しています"));
        Assert.IsTrue(cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("検索中", StringComparison.Ordinal)).Instance.Disabled);
        gate.TrySetResult([new("horse-8dba8ca7-bbc4-4e19-8cd0-2028e12f0085", "検索結果", "検索結果", null, null, 0)]);
        await searchTask;
        StringAssert.Contains(cut.Markup, "検索結果");

        handler.SearchStatus = HttpStatusCode.ServiceUnavailable;
        await ClickAsync(cut, "登録馬を検索");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "登録馬を検索できませんでした。検索語を保持しています"));
        StringAssert.Contains(cut.Markup, "同じ検索語で最後に成功した結果です");
        Assert.AreEqual("保持する検索語", search.Instance.Value);
        StringAssert.Contains(cut.Markup, "horse-8dba8ca7-bbc4-4e19-8cd0-2028e12f0085",
            "A failed same-query refresh may retain results, but must label them as not fresh.");

        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("変更した検索語"));
        Assert.IsFalse(cut.Markup.Contains("horse-8dba8ca7-bbc4-4e19-8cd0-2028e12f0085", StringComparison.Ordinal),
            "Changing the query must immediately clear results from the previous query.");
        await ClickAsync(cut, "登録馬を検索");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "登録馬を検索できませんでした。検索語を保持しています"));
        Assert.AreEqual("変更した検索語", search.Instance.Value);
        Assert.IsFalse(cut.Markup.Contains("horse-8dba8ca7-bbc4-4e19-8cd0-2028e12f0085", StringComparison.Ordinal));
        Assert.AreEqual(3, handler.Requests.Count(request => request.Path == "/api/horses"));
    }

    [TestMethod]
    public async Task AddedSameNameCandidateRemainsUnselectedUntilExplicitHypothesisAcceptance()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        const string addedHorseId = "horse-8dba8ca7-bbc4-4e19-8cd0-2028e12f0085";
        var handler = new MigrationHandler
        {
            SearchHorses = [new(addedHorseId, "ナムラアラバイ", "ナムラアラバイ", "F", null, 0)],
            Preview = StageablePreview(),
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();
        var search = cut.FindComponents<FluentTextField>().Single(field => field.Instance.Label == "登録馬名またはIDで検索");
        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("ナムラアラバイ"));
        await ClickAsync(cut, "登録馬を検索");
        var horseSelector = cut.FindComponents<FluentSelect<string>>().Single();
        await cut.InvokeAsync(() => horseSelector.Instance.ValueChanged.InvokeAsync(addedHorseId));
        var jraInput = cut.FindComponents<FluentTextField>().Single(field => field.Instance.Label == "JRA馬番号（半角数字10桁）");
        await cut.InvokeAsync(() => jraInput.Instance.ValueChanged.InvokeAsync("2024104541"));
        await ClickAsync(cut, "候補へ追加");

        Assert.AreEqual(4, cut.FindAll(".migration-cards[aria-label='馬ID移行の仮説候補'] .migration-card").Count);
        StringAssert.Contains(cut.Markup, "ナムラアラバイ → jra-horse:2024104541");
        StringAssert.Contains(cut.Markup, $"/settings/identity-diagnostics/horses/{addedHorseId}");
        Assert.IsTrue(cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("選択内容をプレビュー", StringComparison.Ordinal)).Instance.Disabled,
            "Adding a same-name mapping must not auto-select or accept its hypothesis.");

        var checkboxes = cut.FindComponents<FluentCheckbox>();
        var target = checkboxes.First(box => box.Markup.Contains($"{addedHorseId} を移行対象として選択", StringComparison.Ordinal));
        var accept = checkboxes.First(box => box.Markup.Contains("jra-horse:2024104541 の未確認仮説", StringComparison.Ordinal));
        await cut.InvokeAsync(() => target.Instance.ValueChanged.InvokeAsync(true));
        Assert.IsTrue(cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("選択内容をプレビュー", StringComparison.Ordinal)).Instance.Disabled);
        await cut.InvokeAsync(() => accept.Instance.ValueChanged.InvokeAsync(true));
        await ClickAsync(cut, "選択内容をプレビュー");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "表示された候補と保存状態を確認してください"));
        var previewRequest = handler.Requests.Single(request => request.Path == "/api/v2/admin/horse-id-migration-previews");
        using var manifestJson = JsonDocument.Parse(previewRequest.Body!);
        var entry = manifestJson.RootElement.GetProperty("entries").EnumerateArray()
            .Single(item => item.GetProperty("oldHorseId").GetString() == addedHorseId);
        Assert.AreEqual("jra-horse:2024104541", entry.GetProperty("jraIdentity").GetString());
        Assert.IsTrue(entry.GetProperty("hypothesisAccepted").GetBoolean());
        Assert.IsFalse(handler.Requests.Any(request => request.Method == HttpMethod.Put),
            "The evidence link is readonly and must not save a candidate profile.");
    }

    [TestMethod]
    public async Task CandidateEditsClearPreviewAndPresetsCanBeRemovedAndRestoredUpToSixteen()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new MigrationHandler { Preview = StageablePreview() };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();
        await SelectAndAcceptFirstCandidateAsync(cut);
        await ClickAsync(cut, "選択内容をプレビュー");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "表示された候補と保存状態を確認してください"));
        await ClickAsync(cut, "この固定内容を実行依頼として保存");
        StringAssert.Contains(cut.Markup, "実行依頼を保存しますか？");
        var acceptance = cut.FindComponents<FluentCheckbox>().First(box => box.Markup.Contains("仮説を確認", StringComparison.Ordinal));
        await cut.InvokeAsync(() => acceptance.Instance.ValueChanged.InvokeAsync(false));
        Assert.IsTrue(cut.FindComponents<FluentDialog>()
            .Single(dialog => dialog.Markup.Contains("実行依頼を保存しますか？", StringComparison.Ordinal))
            .Instance.Hidden);
        Assert.IsFalse(cut.Markup.Contains("表示された候補と保存状態を確認してください", StringComparison.Ordinal));

        for (var index = 0; index < 3; index++)
        {
            var remove = cut.FindComponents<FluentButton>().First(button => button.Markup.Contains("候補から外す", StringComparison.Ordinal));
            await cut.InvokeAsync(() => remove.Instance.OnClick.InvokeAsync());
        }
        Assert.AreEqual(0, cut.FindAll(".migration-cards[aria-label='馬ID移行の仮説候補'] .migration-card").Count);
        await ClickAsync(cut, "参考候補を再追加: ナムラアラバイ");
        Assert.AreEqual(1, cut.FindAll(".migration-cards[aria-label='馬ID移行の仮説候補'] .migration-card").Count);
        Assert.IsTrue(cut.FindComponents<FluentCheckbox>().All(box => !box.Instance.Value));
    }

    [TestMethod]
    public async Task SixteenAddedHorsesAreAllowedAfterRemovingPresets()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var horses = Enumerable.Range(0, 16).Select(index =>
        {
            var horseId = $"horse-{Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"):D}";
            return new HorseRacingPrediction.Contracts.Horses.HorseSummaryDto(horseId, $"Fixture {index}", $"Fixture{index}", null, null, 0);
        }).ToArray();
        var handler = new MigrationHandler { SearchHorses = horses };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();
        for (var index = 0; index < 3; index++) await ClickFirstAsync(cut, "候補から外す");

        var search = cut.FindComponents<FluentTextField>().Single(field => field.Instance.Label == "登録馬名またはIDで検索");
        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("Fixture"));
        await ClickAsync(cut, "登録馬を検索");
        for (var index = 0; index < horses.Length; index++)
        {
            var selector = cut.FindComponents<FluentSelect<string>>().Single();
            await cut.InvokeAsync(() => selector.Instance.ValueChanged.InvokeAsync(horses[index].HorseId));
            var number = (2024104500 + index).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var input = cut.FindComponents<FluentTextField>().Single(field => field.Instance.Label == "JRA馬番号（半角数字10桁）");
            await cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(number));
            await ClickAsync(cut, "候補へ追加");
        }

        Assert.AreEqual(16, cut.FindAll(".migration-cards[aria-label='馬ID移行の仮説候補'] .migration-card").Count);
        Assert.IsTrue(cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("候補へ追加", StringComparison.Ordinal)).Instance.Disabled,
            "Removing all presets must permit 16 new entries, but a seventeenth total candidate must remain unavailable.");
        Assert.AreEqual(1, handler.Requests.Count(request => request.Path == "/api/horses"));
    }

    [TestMethod]
    public async Task CandidateInputRejectsNonAsciiOrNonTenDigitJraNumbers()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        const string horseId = "horse-8dba8ca7-bbc4-4e19-8cd0-2028e12f0085";
        var handler = new MigrationHandler
        {
            SearchHorses = [new(horseId, "追加候補", "追加候補", null, null, 0)],
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();
        var search = cut.FindComponents<FluentTextField>().Single(field => field.Instance.Label == "登録馬名またはIDで検索");
        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("追加候補"));
        await ClickAsync(cut, "登録馬を検索");
        var selector = cut.FindComponents<FluentSelect<string>>().Single();
        await cut.InvokeAsync(() => selector.Instance.ValueChanged.InvokeAsync(horseId));
        var input = cut.FindComponents<FluentTextField>().Single(field => field.Instance.Label == "JRA馬番号（半角数字10桁）");
        foreach (var invalid in new[] { "123", "２０２４１０４５４１", "20241045a1" })
        {
            await cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(invalid));
            Assert.IsTrue(cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("候補へ追加", StringComparison.Ordinal)).Instance.Disabled,
                $"Invalid JRA input should not add a candidate: {invalid}");
        }
        Assert.AreEqual(3, cut.FindAll(".migration-cards[aria-label='馬ID移行の仮説候補'] .migration-card").Count,
            "Invalid JRA numbers must not add a fourth candidate or modify the preserved presets.");
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
        await SelectAndAcceptFirstCandidateAsync(cut);
        await ClickAsync(cut, "選択内容をプレビュー");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "表示された候補と保存状態を確認してください"));
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        Assert.AreEqual("/api/v2/admin/horse-id-migration-previews", handler.Requests[0].Path);
        using var previewManifestDocument = JsonDocument.Parse(handler.Requests[0].Body!);
        var previewManifest = previewManifestDocument.RootElement;
        Assert.AreEqual(OldHorseId, previewManifest.GetProperty("entries")[0].GetProperty("oldHorseId").GetString());
        Assert.AreEqual(JraIdentity, previewManifest.GetProperty("entries")[0].GetProperty("jraIdentity").GetString());
        Assert.IsTrue(previewManifest.GetProperty("entries")[0].GetProperty("hypothesisAccepted").GetBoolean());

        await ClickAsync(cut, "この固定内容を実行依頼として保存");
        StringAssert.Contains(cut.Markup, "実行依頼を保存しますか？");
        Assert.AreEqual(1, handler.Requests.Count, "Opening confirmation must not stage automatically.");
        await ClickAsync(cut, "この内容を実行依頼として保存");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "実行開始前の依頼です"));
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.AreEqual("/api/v2/admin/horse-id-migration-operations", handler.Requests[1].Path);
        using var stageJson = JsonDocument.Parse(handler.Requests[1].Body!);
        var stage = stageJson.RootElement;
        Assert.IsTrue(JsonElement.DeepEquals(previewManifest, stage.GetProperty("manifest")),
            "The staged manifest must be structurally identical to the manifest sent for preview.");
        Assert.AreEqual(OldHorseId, stage.GetProperty("manifest").GetProperty("entries")[0].GetProperty("oldHorseId").GetString());
        Assert.AreEqual(JraIdentity, stage.GetProperty("manifest").GetProperty("entries")[0].GetProperty("jraIdentity").GetString());
        Assert.AreEqual(new string('A', 64), stage.GetProperty("previewFingerprint").GetString());
        Assert.AreEqual(StageablePreview().Fingerprint, stage.GetProperty("previewFingerprint").GetString());
        Assert.IsTrue(Guid.TryParse(stage.GetProperty("requestKey").GetString(), out var requestKey) && requestKey != Guid.Empty,
            "The request key must be persisted before the stage request can lose its response.");
        Assert.IsFalse(handler.Requests.Any(request => request.Method == HttpMethod.Get), "The stage receipt must not trigger status polling.");
        StringAssert.Contains(cut.Markup, "c54c11b0-b17a-4aed-a956-b8feb0c57a74");
        Assert.IsFalse(cut.FindComponents<FluentButton>().Any(button => button.Markup.Contains("この保存済み依頼を実行", StringComparison.Ordinal)),
            "A receipt without the saved manifest and fingerprint must fail closed for execution.");
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
        await SelectAndAcceptFirstCandidateAsync(cut);

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
    public async Task ExplicitExecuteUsesOnlyTheSavedOperationAndManifestAfterCandidateChanges()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var operationId = Guid.Parse("a54c11b0-b17a-4aed-a956-b8feb0c57a74");
        var savedManifest = new HorseIdentityMigrationManifest(
            [new(OldHorseId, JraIdentity, true)]);
        var staged = Operation(operationId, "AwaitingActions", savedManifest, "A".PadLeft(64, 'A'));
        var handler = new MigrationHandler
        {
            Preview = StageablePreview(),
            Staged = staged,
            Executed = Operation(operationId, "Running", savedManifest, new string('A', 64), "IntentRecorded"),
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();
        await SelectAndAcceptFirstCandidateAsync(cut);
        await ClickAsync(cut, "選択内容をプレビュー");
        await ClickAsync(cut, "この固定内容を実行依頼として保存");
        await ClickAsync(cut, "この内容を実行依頼として保存");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, OldHorseId));
        Assert.AreEqual(2, handler.Requests.Count, "Saving a receipt must not execute it.");

        var selection = cut.FindComponents<FluentCheckbox>()
            .First(component => component.Markup.Contains("移行対象として選択", StringComparison.Ordinal));
        await cut.InvokeAsync(() => selection.Instance.ValueChanged.InvokeAsync(false));
        StringAssert.Contains(cut.Find(".migration-saved-request").TextContent, OldHorseId,
            "Editing candidates after staging must not change the saved manifest shown for execution.");

        await ClickAsync(cut, "この保存済み依頼を実行");
        StringAssert.Contains(cut.Markup, "保存済み依頼を実行しますか？");
        var executeDialog = cut.Find("fluent-dialog[aria-labelledby='migration-execute-title']");
        Assert.AreEqual("/jobs", executeDialog.QuerySelector("a")?.GetAttribute("href"));
        StringAssert.Contains(executeDialog.TextContent, "最大10分");
        StringAssert.Contains(executeDialog.TextContent, "15分間");
        StringAssert.Contains(executeDialog.TextContent, "成功後もパイプラインは停止したまま");
        Assert.AreEqual(2, handler.Requests.Count, "Opening the execute confirmation must not dispatch.");
        await ClickAsync(cut, "この依頼を実行");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "開始受付済み"));
        Assert.AreEqual(3, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[2].Method);
        Assert.AreEqual($"/api/v2/admin/horse-id-migration-operations/{operationId:D}/execute", handler.Requests[2].Path);
        Assert.IsNull(handler.Requests[2].Body, "Execution must send the saved operation ID, not an editable manifest.");
    }

    [TestMethod]
    public async Task UncertainExecuteRequiresManualStatusAndSucceededWithoutVerifiedPhaseIsNotSuccess()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var operationId = Guid.Parse("b54c11b0-b17a-4aed-a956-b8feb0c57a74");
        var manifest = new HorseIdentityMigrationManifest([new(OldHorseId, JraIdentity, true)]);
        var handler = new MigrationHandler
        {
            Status = Operation(operationId, "Succeeded", manifest, new string('A', 64), "DataSucceeded"),
            Preview = StageablePreview(),
            Staged = Operation(operationId, "AwaitingActions", manifest, new string('A', 64)),
            ExecuteStatus = HttpStatusCode.ServiceUnavailable,
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();

        await SelectAndAcceptFirstCandidateAsync(cut);
        await ClickAsync(cut, "選択内容をプレビュー");
        await ClickAsync(cut, "この固定内容を実行依頼として保存");
        await ClickAsync(cut, "この内容を実行依頼として保存");
        await ClickAsync(cut, "この保存済み依頼を実行");
        await ClickAsync(cut, "この依頼を実行");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "実行要求の受付結果を確認できませんでした"));
        Assert.AreEqual(3, handler.Requests.Count, "A lost execute response must not be automatically resent or queried.");
        Assert.IsFalse(cut.FindComponents<FluentButton>().Any(button => button.Markup.Contains("この保存済み依頼を実行", StringComparison.Ordinal)));
        await ClickAsync(cut, "状態を更新");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "データ移行済み・検証中"));
        Assert.IsFalse(cut.Markup.Contains("検証済み完了", StringComparison.Ordinal));
        Assert.IsFalse(cut.Markup.Contains("検証を実行", StringComparison.Ordinal),
            "VerifyOnly is owned by the minimal host, not duplicated in the normal Razor page.");

        Assert.AreEqual(4, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Get, handler.Requests[3].Method,
            "After uncertainty, the only follow-up is an explicit read-only status request.");
    }

    [TestMethod]
    public async Task LostStageResponseRecoversByManualRequestKeyLookupWithoutResending()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var operationId = Guid.Parse("c54c11b0-b17a-4aed-a956-b8feb0c57a74");
        var savedManifest = new HorseIdentityMigrationManifest([new(OldHorseId, JraIdentity, true)]);
        var handler = new MigrationHandler
        {
            Preview = StageablePreview(),
            StageStatus = HttpStatusCode.ServiceUnavailable,
            RequestReceipt = Operation(operationId, "AwaitingActions", savedManifest, new string('A', 64)),
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<HorseIdentityMigrationAdmin>();
        await SelectAndAcceptFirstCandidateAsync(cut);
        await ClickAsync(cut, "選択内容をプレビュー");
        await ClickAsync(cut, "この固定内容を実行依頼として保存");
        await ClickAsync(cut, "この内容を実行依頼として保存");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "受付照会キーで状態を手動確認してください"));
        Assert.AreEqual(2, handler.Requests.Count, "A lost stage response must not trigger an automatic lookup or retry.");
        using var stageJson = JsonDocument.Parse(handler.Requests[1].Body!);
        var requestKey = stageJson.RootElement.GetProperty("requestKey").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(requestKey));
        StringAssert.Contains(context.Services.GetRequiredService<NavigationManager>().Uri, $"requestKey={requestKey}");

        await using var recoveredContext = CreateContext(app.Services, http);
        recoveredContext.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"http://localhost/settings/horse-id-migration?requestKey={requestKey}");
        var recoveredCut = recoveredContext.Render<HorseIdentityMigrationAdmin>();
        Assert.AreEqual(2, handler.Requests.Count, "Reloading with a request key only restores the lookup field.");
        await ClickAsync(recoveredCut, "状態を更新");

        recoveredCut.WaitForAssertion(() => StringAssert.Contains(recoveredCut.Markup, operationId.ToString("D")));
        Assert.AreEqual(3, handler.Requests.Count, "Recovery reads the journal receipt and does not repeat the stage POST.");
        Assert.AreEqual(HttpMethod.Get, handler.Requests[2].Method);
        Assert.AreEqual($"/api/v2/admin/horse-id-migration-requests/{requestKey}", handler.Requests[2].Path);
        StringAssert.Contains(recoveredContext.Services.GetRequiredService<NavigationManager>().Uri, $"operationId={operationId:D}");
        Assert.IsFalse(recoveredContext.Services.GetRequiredService<NavigationManager>().Uri.Contains("requestKey=", StringComparison.Ordinal));
        Assert.IsTrue(recoveredCut.FindComponents<FluentButton>().Any(button => button.Markup.Contains("この保存済み依頼を実行", StringComparison.Ordinal)));
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
        await SelectAndAcceptFirstCandidateAsync(cut);
        await ClickAsync(cut, "選択内容をプレビュー");
        await ClickAsync(cut, "この固定内容を実行依頼として保存");
        await ClickAsync(cut, "この内容を実行依頼として保存");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "最新の状態をもう一度プレビューしてください"));
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(cut.FindComponents<FluentCheckbox>().First(box => box.Markup.Contains("移行対象として選択", StringComparison.Ordinal)).Instance.Value,
            "A stale preview must not clear the operator's explicit selection.");
        Assert.IsFalse(cut.Markup.Contains("実行依頼を保存しました", StringComparison.Ordinal));
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        StringAssert.Contains(navigation.Uri, "requestKey=");
        await ClickAsync(cut, "拒否された受付キーを破棄して再プレビュー");
        Assert.IsFalse(navigation.Uri.Contains("requestKey=", StringComparison.Ordinal));
        Assert.IsTrue(cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("選択内容をプレビュー", StringComparison.Ordinal)).Instance.Disabled == false,
            "After a definitive rejection, explicit abandonment returns the operator to a fresh preview instead of retrying the stage.");
        Assert.AreEqual(2, handler.Requests.Count, "Abandoning a rejected receipt key must not send another request.");
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
        await SelectAndAcceptFirstCandidateAsync(cut);

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

    private static async Task ClickFirstAsync(IRenderedComponent<HorseIdentityMigrationAdmin> cut, string label)
    {
        var button = cut.FindComponents<FluentButton>().First(component => component.Markup.Contains(label, StringComparison.Ordinal));
        await cut.InvokeAsync(() => button.Instance.OnClick.InvokeAsync());
    }

    private static async Task SelectAndAcceptFirstCandidateAsync(IRenderedComponent<HorseIdentityMigrationAdmin> cut)
    {
        var checkboxes = cut.FindComponents<FluentCheckbox>();
        var target = checkboxes.First(component => component.Markup.Contains("移行対象として選択", StringComparison.Ordinal));
        var hypothesis = checkboxes.First(component => component.Markup.Contains("仮説を確認", StringComparison.Ordinal));
        await cut.InvokeAsync(() => target.Instance.ValueChanged.InvokeAsync(true));
        await cut.InvokeAsync(() => hypothesis.Instance.ValueChanged.InvokeAsync(true));
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

    private static HorseIdentityMigrationOperationResponse Operation(Guid operationId, string status,
        HorseIdentityMigrationManifest manifest, string fingerprint, string? phase = null) =>
        new(operationId, status, "None", DateTimeOffset.Parse("2026-10-19T00:00:00Z"),
            DateTimeOffset.Parse("2026-10-19T00:01:00Z"), [])
        {
            SavedManifest = manifest,
            PreviewFingerprint = fingerprint,
            ExecutionPhase = phase,
            ExecutionUpdatedAt = phase is null ? null : DateTimeOffset.Parse("2026-10-19T00:01:00Z"),
        };

    private static HorseIdentityMigrationSchemaDiagnostics SchemaDiagnostics(bool complete = true) => new(
        DateTimeOffset.Parse("2026-10-17T01:02:03Z"), complete,
        [new(HorseIdentityMigrationSchemaSurface.EventStore,
            complete ? HorseIdentityMigrationSchemaStatus.Observed : HorseIdentityMigrationSchemaStatus.Incomplete,
            1, 0, new string('a', 64), [], [])]);

    private sealed class MigrationHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];
        public HorseIdentityMigrationPreview? Preview { get; init; }
        public HorseIdentityMigrationOperationResponse? Staged { get; init; }
        public HorseIdentityMigrationOperationResponse? Status { get; init; }
        public HorseIdentityMigrationOperationResponse? RequestReceipt { get; init; }
        public HorseIdentityMigrationOperationResponse? Executed { get; init; }
        public HttpStatusCode? ExecuteStatus { get; init; }
        public HorseIdentityMigrationSchemaDiagnostics? SchemaDiagnostics { get; init; }
        public IReadOnlyList<HorseRacingPrediction.Contracts.Horses.HorseSummaryDto> SearchHorses { get; init; } = [];
        public HttpStatusCode? StageStatus { get; init; }
        public HttpStatusCode? SchemaDiagnosticsStatus { get; set; }
        public HttpStatusCode? SearchStatus { get; set; }
        public TaskCompletionSource<HorseIdentityMigrationPreview>? PreviewPending { get; init; }
        public TaskCompletionSource<HorseIdentityMigrationSchemaDiagnostics>? SchemaDiagnosticsPending { get; init; }
        public TaskCompletionSource<IReadOnlyList<HorseRacingPrediction.Contracts.Horses.HorseSummaryDto>>? SearchHorsesPending { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new(request.Method, path, body));
            if (path.EndsWith("/execute", StringComparison.Ordinal) && ExecuteStatus is { } executeStatus)
                return new HttpResponseMessage(executeStatus);
            if (path.EndsWith("/execute", StringComparison.Ordinal) && Executed is { } executed)
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent(JsonSerializer.Serialize(executed, Json), System.Text.Encoding.UTF8, "application/json"),
                };
            if (path == "/api/v2/admin/horse-id-migration-operations" && StageStatus is { } stageStatus)
                return new HttpResponseMessage(stageStatus)
                {
                    Content = new StringContent("{\"code\":\"PreviewStale\"}", System.Text.Encoding.UTF8, "application/json"),
                };
            if (path == "/api/v2/admin/horse-id-migration-schema-diagnostics" && SchemaDiagnosticsStatus is { } diagnosticsStatus)
                return new HttpResponseMessage(diagnosticsStatus);
            if (path == "/api/horses" && SearchStatus is { } searchStatus)
                return new HttpResponseMessage(searchStatus);

            object? response = path switch
            {
                "/api/v2/admin/horse-id-migration-schema-diagnostics" when request.Method == HttpMethod.Get => SchemaDiagnosticsPending is { } diagnosticsPending
                    ? await diagnosticsPending.Task.WaitAsync(cancellationToken) : SchemaDiagnostics,
                "/api/horses" when request.Method == HttpMethod.Get => new HorseRacingPrediction.Contracts.Horses.SearchHorsesResponse(
                    SearchHorsesPending is { } searchPending ? await searchPending.Task.WaitAsync(cancellationToken) : SearchHorses,
                    new HorseRacingPrediction.Contracts.Common.PaginationDto(1, 50,
                        SearchHorsesPending?.Task.IsCompletedSuccessfully == true ? SearchHorsesPending.Task.Result.Count : SearchHorses.Count,
                        SearchHorsesPending?.Task.IsCompletedSuccessfully == true && SearchHorsesPending.Task.Result.Count > 0 || SearchHorses.Count > 0 ? 1 : 0)),
                "/api/v2/admin/horse-id-migration-previews" => PreviewPending is { } pending
                    ? await pending.Task.WaitAsync(cancellationToken) : Preview,
                "/api/v2/admin/horse-id-migration-operations" => Staged,
                _ when path.StartsWith("/api/v2/admin/horse-id-migration-requests/", StringComparison.Ordinal) => RequestReceipt,
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
