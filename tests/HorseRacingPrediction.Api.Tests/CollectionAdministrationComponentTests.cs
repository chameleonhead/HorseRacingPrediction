using Bunit;
using HorseRacingPrediction.Api.CollectionController;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using HorseRacingPrediction.Api.Web.Components.Pages;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Common.Time;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;
using System.Net.Http.Json;
using System.Text.Json;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionAdministrationComponentTests
{
    [TestMethod]
    public async Task Jobs_ShowsEveryLaneWithAuthoritativeActivityTimestamps()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var started = new DateTimeOffset(2026, 10, 2, 23, 39, 0, TimeSpan.FromHours(9));
        var handler = new ResourceHandler
        {
            LaneActivity =
            [
                new(CollectionLane.Realtime, 584, 2, started, started.AddMinutes(1)),
                new(CollectionLane.Normal, 2427, 1, started.AddMinutes(-1), started),
                new(CollectionLane.Background, 541, 0, null, null)
            ]
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Jobs>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "処理区分別の実行状況"));
        var grid = cut.Find("[aria-label='処理区分別の実行状況']");
        StringAssert.Contains(grid.TextContent, "リアルタイム");
        StringAssert.Contains(grid.TextContent, "通常");
        StringAssert.Contains(grid.TextContent, "バックグラウンド");
        StringAssert.Contains(grid.TextContent, "584");
        StringAssert.Contains(grid.TextContent, "2,427");
        StringAssert.Contains(grid.TextContent, "2026/10/02 23:39:00");
        StringAssert.Contains(grid.TextContent, "実績なし");

        handler.LaneActivity =
        [
            new(CollectionLane.Realtime, 10, 1, started.AddMinutes(5), started.AddMinutes(4)),
            new(CollectionLane.Normal, 20, 0, started, started),
            new(CollectionLane.Background, 30, 1, started.AddMinutes(3), started.AddMinutes(2))
        ];
        await ClickFluentButtonAsync(cut, "更新");

        cut.WaitForAssertion(() =>
        {
            var refreshed = cut.Find("[aria-label='処理区分別の実行状況']").TextContent;
            StringAssert.Contains(refreshed, "2026/10/02 23:44:00");
            StringAssert.Contains(refreshed, "30");
        });
    }

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
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        using var http = new HttpClient(new ResourceHandler { EmptyPlatform = true })
        { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Jobs>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "表示する収集処理はありません"));
        await ClickFluentButtonAsync(cut, "収集を依頼");
        await ClickButtonAsync(cut, "複数をまとめて再取得");
        var definitionOptions = cut.Find("select[aria-label='情報の種類']").QuerySelectorAll("option")
            .Select(x => x.TextContent.Trim()).ToArray();
        CollectionAssert.Contains(definitionOptions, "レース詳細");
        CollectionAssert.DoesNotContain(definitionOptions, "出馬表");
        CollectionAssert.DoesNotContain(definitionOptions, "レース結果");
        await SelectAsync(cut, "対象の選び方", "SpecificResources");
        cut.WaitForAssertion(() => Assert.IsTrue(cut.FindComponents<FluentButton>()
            .Last(x => x.Markup.Contains("再取得を依頼</")).Instance.Disabled));
        StringAssert.Contains(cut.Markup, "確認後に対象が増えることはありません");
    }

    [TestMethod]
    public async Task FailureGroups_ShowComparableFieldsAndAllGroupsLinkForLongValues()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var now = new DateTimeOffset(2026, 9, 14, 14, 20, 0, TimeSpan.FromHours(9));
        var groups = Enumerable.Range(1, 7).Select(index => new CollectionFailureGroup(
            $"group-{index}", new("race-detail"), CollectionTaskStatus.Failed,
            index == 1 ? "UnexpectedPageWithAnExtremelyLongClassificationWithoutSpaces" : $"Error{index}",
            index == 1 ? new string('長', 120) : $"障害内容 {index}", 20 - index,
            now.AddHours(-index), now.AddMinutes(-index), [], [])).ToArray();
        var handler = new ResourceHandler { FailureGroups = groups };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);

        var cut = context.Render<Jobs>();

        cut.WaitForAssertion(() => Assert.HasCount(6, cut.FindAll("a.failure-group")));
        var first = cut.Find("a.failure-group");
        StringAssert.Contains(first.TextContent, "UnexpectedPageWithAnExtremelyLongClassificationWithoutSpaces");
        StringAssert.Contains(first.TextContent, "レース詳細の収集");
        StringAssert.Contains(first.TextContent, "19 件");
        StringAssert.Contains(first.TextContent, "最新発生");
        StringAssert.Contains(first.TextContent, "詳細を見る");
        StringAssert.Contains(first.GetAttribute("aria-label"), "対象19件の詳細を見る");
        var all = cut.Find("a.failure-groups-all");
        StringAssert.Contains(all.TextContent, "すべての障害（7 原因）を見る");
        Assert.AreEqual("/jobs/operations?tab=failures", all.GetAttribute("href"));
    }

    [TestMethod]
    public async Task FailureGroupRecovery_SendsCompleteMembershipAndShowsReceipt()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new FailureGroupHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<CollectionFailureGroupDetail>(parameters => parameters
            .Add(x => x.GroupKey, FailureGroupHandler.GroupKey));

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "対象をまとめて再取得"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains(">対象をまとめて再取得</")).Instance.OnClick.InvokeAsync());
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains(">再取得を依頼</")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "新しいタスク 2 件を作成しました"));
        CollectionAssert.AreEquivalent(handler.NotificationIds.ToArray(),
            handler.ExpectedNotificationIds?.ToArray());
    }

    [TestMethod]
    public async Task FailureGroupRecovery_MembershipConflictRequestsRefreshWithoutSuccessMessage()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new FailureGroupHandler { MembershipChanged = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<CollectionFailureGroupDetail>(parameters => parameters
            .Add(x => x.GroupKey, FailureGroupHandler.GroupKey));

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "対象をまとめて再取得"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains(">対象をまとめて再取得</")).Instance.OnClick.InvokeAsync());
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains(">再取得を依頼</")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup,
            "対象が更新されたため、画面を更新してもう一度確認してください"));
        Assert.IsFalse(cut.Markup.Contains("新しいタスク", StringComparison.Ordinal));
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
        Assert.IsTrue(handler.LatestOnlyRequests > 0);
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
        await cut.Find("input[aria-label='対象ID（カンマ区切り）']").InputAsync("H001");
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Single(x => x.Markup.Contains("対象を確認</")).Instance.OnClick.InvokeAsync());
        cut.WaitForAssertion(() => Assert.IsFalse(cut.FindComponents<FluentButton>()
            .Last(x => x.Markup.Contains("再取得を依頼</")).Instance.Disabled));
        Assert.AreEqual(1, handler.Previews);
        Assert.AreEqual(CollectionDefinitionRevisions.HorseProfile,
            handler.LastBulkPreviewRequest?.Selection?.RequestedRevision);
    }

    [TestMethod]
    public async Task StatusNavigation_UsesFluentTabsAndChangesThePersistedView()
    {
        var (app, original) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var ignored = original;
        var handler = new ResourceHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await using var context = CreateContext(app.Services, http);
        var cut = context.Render<Jobs>();

        cut.WaitForAssertion(() => Assert.HasCount(1, cut.FindComponents<FluentTabs>()));
        var tabs = cut.FindComponent<FluentTabs>();
        Assert.IsTrue(tabs.Instance.ShowActiveIndicator);
        var activePanel = cut.Find("#attention-panel");
        StringAssert.Contains(activePanel.InnerHtml, "収集処理の絞り込み");
        CollectionAssert.IsSubsetOf(new[] { "要対応", "処理中", "待機中", "最近完了", "収集対象", "最新の処理" },
            cut.FindComponents<FluentTab>().Select(x => x.Instance.Label?.ToString()?.Split("  ")[0]).ToArray());
        Assert.AreEqual(1, handler.TaskSearchRequests);
        Assert.AreEqual(1, handler.TaskViewCountRequests);

        await cut.InvokeAsync(() => tabs.Instance.ActiveTabIdChanged.InvokeAsync("waiting"));
        cut.WaitForAssertion(() => StringAssert.Contains(
            context.Services.GetRequiredService<NavigationManager>().Uri, "view=waiting"));
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
        Assert.AreEqual(CollectionResourceType.Horse, handler.LastManualRequest.ResourceType);
        Assert.AreEqual("H002", handler.LastManualRequest.ResourceId);
        Assert.AreEqual("horse-profile", handler.LastManualRequest.DefinitionId);
        Assert.AreEqual(CollectionDefinitionRevisions.HorseProfile, handler.LastManualRequest.RequestedRevision);
    }

    [TestMethod]
    public async Task RaceQuickAction_CreatesOnlyUnifiedRaceDetailRequest()
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
        StringAssert.Contains(cut.Markup, "レース詳細");
        Assert.IsFalse(cut.FindAll("button").Any(x => x.TextContent.Trim() is "出馬表" or "レース結果"));
        await ClickButtonAsync(cut, "レース詳細");
        var target = cut.FindComponents<FluentTextField>().Single(x => x.Instance.Label?.ToString() == "対象ID");
        await cut.InvokeAsync(() => target.Instance.ValueChanged.InvokeAsync("20260912:Nakayama:11"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Last(x => x.Markup.Contains("収集を依頼</")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => Assert.AreEqual(1, handler.ManualRequests));
        Assert.IsNotNull(handler.LastManualRequest);
        Assert.AreEqual(CollectionResourceType.Race, handler.LastManualRequest.ResourceType);
        Assert.AreEqual("race-detail", handler.LastManualRequest.DefinitionId);
        Assert.AreEqual(CollectionDefinitionRevisions.RaceDetail, handler.LastManualRequest.RequestedRevision);
        Assert.AreEqual(CollectionLane.Realtime, handler.LastManualRequest.Lane);
        Assert.AreEqual(100, handler.LastManualRequest.Priority);
    }

    [TestMethod]
    public async Task RacePeriodRecollection_PreviewsInclusiveDays_AndShowsAcceptedBatchLink()
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
        await ClickButtonAsync(cut, "期間を指定してレースを再取得");
        cut.Find("input[aria-label='開始日']").Change("2026-09-12");
        cut.Find("input[aria-label='終了日']").Change("2026-09-13");
        Assert.IsTrue(cut.FindComponents<FluentButton>()
            .First(x => x.Markup.Contains("再取得を依頼</")).Instance.Disabled);

        await ClickFluentButtonAsync(cut, "対象を確認");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "2 日間"));
        Assert.AreEqual(1, handler.RacePeriodPreviews);
        Assert.IsFalse(cut.FindComponents<FluentButton>()
            .First(x => x.Markup.Contains("再取得を依頼</")).Instance.Disabled);
        await ClickFluentButtonAsync(cut, "再取得を依頼");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "再取得を受け付けました"));
        StringAssert.Contains(cut.Markup, "新規 2 件、既存 0 件");
        var link = cut.FindAll("a").Single(x => x.TextContent.Contains("進捗を確認"));
        StringAssert.Contains(link.GetAttribute("href"), "/jobs/backfills/recollection%3Acomponent-test");
        Assert.AreEqual(1, handler.RacePeriodRequests);
        Assert.AreEqual(new DateOnly(2026, 9, 12), handler.LastRacePeriodRequest?.From);
        Assert.AreEqual(new DateOnly(2026, 9, 13), handler.LastRacePeriodRequest?.To);
        Assert.IsFalse(string.IsNullOrWhiteSpace(handler.LastRacePeriodRequest?.BatchId));
    }

    [TestMethod]
    public async Task RacePeriodRecollection_InvalidRanges_DoNotCallPreviewAndPreserveInputs()
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
        await ClickButtonAsync(cut, "期間を指定してレースを再取得");
        var from = cut.Find("input[aria-label='開始日']");
        var to = cut.Find("input[aria-label='終了日']");

        from.Change("2026-09-13");
        to.Change("2026-09-12");
        await ClickFluentButtonAsync(cut, "対象を確認");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "開始日は終了日以前にしてください"));
        Assert.AreEqual("2026-09-13", from.GetAttribute("value"));

        var today = JstTime.Today();
        from.Change(today.AddDays(-31).ToString("yyyy-MM-dd"));
        to.Change(today.ToString("yyyy-MM-dd"));
        await ClickFluentButtonAsync(cut, "対象を確認");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "期間は31日以内にしてください"));

        from.Change(today.ToString("yyyy-MM-dd"));
        to.Change(today.AddDays(1).ToString("yyyy-MM-dd"));
        await ClickFluentButtonAsync(cut, "対象を確認");
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "未来日のレースは再取得できません"));
        Assert.AreEqual(0, handler.RacePeriodPreviews);
        Assert.AreEqual(0, handler.RacePeriodRequests);
    }

    [TestMethod]
    public async Task ExplicitUrlAction_RequiresOnlyUrlAndUsesIdentificationEndpoint()
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
        await ClickButtonAsync(cut, "URLから収集");
        var url = cut.FindComponents<FluentTextField>()
            .Single(x => x.Instance.Label?.ToString() == "JRAページのURL");
        await cut.InvokeAsync(() => url.Instance.ValueChanged.InvokeAsync(
            "https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604011120260905/2F"));
        await cut.InvokeAsync(() => cut.FindComponents<FluentButton>()
            .Last(x => x.Markup.Contains("収集を依頼</")).Instance.OnClick.InvokeAsync());

        cut.WaitForAssertion(() => Assert.AreEqual(1, handler.ExplicitUrlRequests));
        Assert.AreEqual("https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202604011120260905/2F",
            handler.LastExplicitUrlRequest?.Url);
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

    private static Task ClickButtonAsync(IRenderedComponent<Jobs> cut, string text) => cut.InvokeAsync(async () =>
    {
        var tab = cut.FindComponents<FluentTab>()
            .FirstOrDefault(x => x.Instance.Label?.ToString()?.StartsWith(text, StringComparison.Ordinal) == true);
        if (tab is not null)
        {
            await cut.FindComponent<FluentTabs>().Instance.ActiveTabIdChanged.InvokeAsync(tab.Instance.Id);
            return;
        }
        cut.FindAll("button").First(x => x.TextContent.Trim().StartsWith(text, StringComparison.Ordinal)).Click();
    });

    private static Task SelectAsync(IRenderedComponent<Jobs> cut, string label, string value) => cut.InvokeAsync(() =>
        cut.Find($"select[aria-label='{label}']").Change(value));

    private sealed class ResourceHandler : HttpMessageHandler
    {
        public bool EmptyPlatform { get; init; }
        public IReadOnlyList<CollectionFailureGroup> FailureGroups { get; init; } = [];
        public IReadOnlyList<CollectionLaneActivity> LaneActivity { get; set; } = [];
        private static readonly ResourceKey Resource = new(CollectionResourceType.Horse, "jra", "H001");
        private static readonly CollectionDefinitionId Definition = new("horse-profile");
        public int ManualRequests { get; private set; }
        public int Previews { get; private set; }
        public int ExplicitUrlRequests { get; private set; }
        public int RacePeriodPreviews { get; private set; }
        public int RacePeriodRequests { get; private set; }
        public int TaskSearchRequests { get; private set; }
        public int TaskViewCountRequests { get; private set; }
        public int LatestOnlyRequests { get; private set; }
        public CreateCollectionRequest? LastManualRequest { get; private set; }
        public PreviewCollectionTaskBatchRequest? LastBulkPreviewRequest { get; private set; }
        public CreateExplicitUrlCollectionRequest? LastExplicitUrlRequest { get; private set; }
        public CreateRacePeriodRecollectionRequest? LastRacePeriodRequest { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/admin/collection/tasks")
            {
                TaskSearchRequests++;
                if (request.RequestUri.Query.Contains("latestOnly=true", StringComparison.OrdinalIgnoreCase))
                    LatestOnlyRequests++;
            }
            if (request.RequestUri.AbsolutePath == "/api/v2/admin/collection/operations/task-view-counts")
                TaskViewCountRequests++;
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/task-batch-previews"))
            {
                Previews++;
                LastBulkPreviewRequest = await request.Content!.ReadFromJsonAsync<PreviewCollectionTaskBatchRequest>(cancellationToken);
                return await Ok(new { preview = new CollectionBulkPreview(Definition, 1, 1, [Resource]) });
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/recollection-previews"))
            {
                RacePeriodPreviews++;
                var input = await request.Content!.ReadFromJsonAsync<PreviewRacePeriodRecollectionRequest>(cancellationToken);
                LastRacePeriodRequest = new(input!.Period!.From, input.Period.To, input.Period.Provider,
                    input.Period.BatchId);
                var preview = new RacePeriodRecollectionPreview(LastRacePeriodRequest!.From,
                    LastRacePeriodRequest.To, LastRacePeriodRequest.To.DayNumber - LastRacePeriodRequest.From.DayNumber + 1,
                    "JRA");
                return await Ok(new { preview });
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/recollection-batches"))
            {
                RacePeriodRequests++;
                var envelope = await request.Content!.ReadFromJsonAsync<CreateRecollectionBatchRequest>(cancellationToken);
                var batchRequest = envelope!.Batch;
                Assert.AreEqual("RacePeriod", batchRequest!.Mode);
                LastRacePeriodRequest = new(batchRequest.From!.Value, batchRequest.To!.Value,
                    batchRequest.Provider!, batchRequest.BatchId);
                var batch = new BackfillBatchSnapshot("recollection:component-test", LastRacePeriodRequest!.From,
                    LastRacePeriodRequest.To, 2, 2, 2, 0, 0, 0, [], DateTimeOffset.UtcNow, null);
                return await Ok(new
                {
                    batch = new CollectionRecollectionBatchResponse("RacePeriod", null,
                    new RacePeriodRecollectionReceipt(batch, 2, 0))
                });
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v2/admin/collection/tasks")
            {
                var json = await request.Content!.ReadAsStringAsync(cancellationToken);
                var envelope = JsonSerializer.Deserialize<CreateCollectionTaskRequest>(
                    json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var submission = envelope?.Task;
                if (string.Equals(submission?.Mode, "SourceUrl", StringComparison.Ordinal))
                {
                    ExplicitUrlRequests++;
                    LastExplicitUrlRequest = new(submission!.SourceUrl!.Url);
                    return await Ok(new
                    {
                        submission = new CollectionTaskSubmissionResponse("SourceUrl",
                        new CollectionRequestReceipt(Guid.NewGuid(), Guid.NewGuid(), true), Resource, Definition,
                        JstTime.Today(), LastExplicitUrlRequest.Url, new Dictionary<string, string>())
                    });
                }
                ManualRequests++;
                var resourceRequest = submission!.Resource!;
                LastManualRequest = new(resourceRequest.ResourceType, resourceRequest.Provider,
                    resourceRequest.ResourceId, resourceRequest.DefinitionId, resourceRequest.RequestedRevision,
                    resourceRequest.Reason, resourceRequest.Lane, resourceRequest.Priority,
                    resourceRequest.ExplicitUrl, resourceRequest.BatchId, resourceRequest.EffectiveDate,
                    resourceRequest.Attributes);
                return await Ok(new
                {
                    submission = new CollectionTaskSubmissionResponse("Resource",
                    new CollectionRequestReceipt(Guid.NewGuid(), Guid.NewGuid(), true),
                    new(resourceRequest.ResourceType, resourceRequest.Provider, resourceRequest.ResourceId),
                    new(resourceRequest.DefinitionId), resourceRequest.EffectiveDate, resourceRequest.ExplicitUrl,
                    resourceRequest.Attributes ?? new Dictionary<string, string>())
                });
            }
            object value = request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/admin/collection/tasks" when request.RequestUri.Query.Contains("limit=", StringComparison.OrdinalIgnoreCase) => new
                {
                    page = new CollectionTaskPage(1, 1, 50,
                    [new CollectionTaskSummary(Guid.NewGuid(), Resource, Definition, CollectionTaskStatus.Pending,
                        CollectionLane.Normal, 50, 1, DateTimeOffset.UtcNow, 0)])
                },
                "/api/v2/admin/collection/tasks" when EmptyPlatform => new { page = new CollectionTaskPage(0, 1, 50, []) },
                "/api/v2/admin/collection/tasks" => new
                {
                    page = new CollectionTaskPage(1, 1, 50,
                    [new CollectionTaskSummary(Guid.NewGuid(), Resource, Definition, CollectionTaskStatus.Pending,
                        CollectionLane.Normal, 50, 1, DateTimeOffset.UtcNow, 0)])
                },
                "/api/v2/admin/collection/states" => new
                {
                    page = new CollectionStatePage(1, 1, 50,
                    [new CollectionStateSnapshot(Resource, Definition, 1, 1, DateTimeOffset.UtcNow, null,
                        CollectionStateStatus.Current)])
                },
                "/api/v2/admin/collection/operations/progress" when EmptyPlatform => new
                {
                    progress = new CollectionProgressSnapshot(
                    new Dictionary<CollectionResourceType, int>(), new Dictionary<CollectionStateStatus, int>(),
                    new Dictionary<CollectionLane, int>(), new Dictionary<int, int>(),
                    new Dictionary<string, int>(), 0, LaneActivity)
                },
                "/api/v2/admin/collection/operations/progress" => new
                {
                    progress = new CollectionProgressSnapshot(
                    new Dictionary<CollectionResourceType, int> { [CollectionResourceType.Horse] = 1 },
                    new Dictionary<CollectionStateStatus, int> { [CollectionStateStatus.Pending] = 1 },
                    new Dictionary<CollectionLane, int>(), new Dictionary<int, int>(),
                    new Dictionary<string, int>(), 0, LaneActivity)
                },
                "/api/v2/admin/collection/operations/task-view-counts" => new
                {
                    counts = new CollectionTaskViewCounts(
                    new Dictionary<string, int>
                    {
                        ["attention"] = 0,
                        ["running"] = 0,
                        ["waiting"] = 1,
                        ["recent"] = 0,
                        ["all"] = 1
                    })
                },
                "/api/v2/admin/collection/pipeline-state" => new { pipeline = new HorseRacingPrediction.CollectionOperations.CollectionPlatform.CollectionPipelineState(false, null, DateTimeOffset.UtcNow) },
                "/api/v2/admin/collection/failure-notifications" => new { notifications = Array.Empty<PendingCollectionFailureNotification>() },
                "/api/v2/admin/collection/failure-notification-groups" => new { groups = FailureGroups },
                "/api/v2/admin/collection/backfill-batches" => new { batches = Array.Empty<BackfillBatchSnapshot>() },
                _ when request.RequestUri.AbsolutePath.StartsWith("/api/v2/admin/collection/resources/") =>
                    new
                    {
                        resource = new CollectionResourceDetail(new(Resource, Definition, 0, 1, null, null,
                        CollectionStateStatus.Pending), [], [], [], [])
                    },
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

    private sealed class FailureGroupHandler : HttpMessageHandler
    {
        public const string GroupKey = "failure-group-component";
        public IReadOnlyList<Guid> NotificationIds { get; } = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        public IReadOnlyList<Guid>? ExpectedNotificationIds { get; private set; }
        public bool MembershipChanged { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath.EndsWith($"/failure-notification-groups/{GroupKey}"))
            {
                var now = DateTimeOffset.Parse("2026-10-02T10:00:00+09:00");
                var definition = new CollectionDefinitionId("horse-profile");
                var group = new CollectionFailureGroup(GroupKey, definition, CollectionTaskStatus.Failed,
                    "HttpRequestException", "request failed", NotificationIds.Count, now, now,
                    NotificationIds, []);
                var items = NotificationIds.Take(2).Select((id, index) => new CollectionFailureTarget(id,
                    Guid.NewGuid(), new(CollectionResourceType.Horse, "jra", $"H{index + 1:D3}"), definition,
                    CollectionTaskStatus.Failed, "HttpRequestException", "request failed", 1, now,
                    null, null, null, null, null, null)).ToArray();
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        page = new CollectionFailureGroupPage(group,
                            NotificationIds.Count, 1, 50, (string?)null, items)
                    })
                };
            }

            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/recovery-batches"))
            {
                var envelope = await request.Content!.ReadFromJsonAsync<CreateCollectionRecoveryBatchRequest>(
                    cancellationToken);
                Assert.AreEqual("GroupKey", envelope?.Recovery?.SelectorType);
                Assert.AreEqual(GroupKey, envelope?.Recovery?.GroupKey);
                ExpectedNotificationIds = envelope?.Recovery?.ExpectedNotificationIds;
                if (MembershipChanged)
                    return new HttpResponseMessage(System.Net.HttpStatusCode.Conflict)
                    {
                        Content = JsonContent.Create(new { message = "Failure group membership changed." })
                    };
                return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
                {
                    Content = JsonContent.Create(new
                    {
                        recovery = new CollectionFailureRecoveryResult(NotificationIds.Count, 2, 1, [Guid.NewGuid(), Guid.NewGuid()])
                    })
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
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
