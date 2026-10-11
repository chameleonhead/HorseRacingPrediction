using System.Net;
using System.Net.Http.Json;
using Bunit;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.ApiClient.Races;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Races;
using HorseRacingPrediction.Web.Components.Pages.Races;
using HorseRacingPrediction.Web.Components.Races;
using HorseRacingPrediction.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HorseRacingPrediction.Web.Tests;

[TestClass]
public sealed class RacePagesComponentTests
{
    [TestMethod]
    public void RaceList_RendersApiResultsAndPagination()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);

        var cut = context.Render<Races>();

        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "テストレース");
            StringAssert.Contains(cut.Markup, "1 / 2 ページ");
            Assert.AreEqual("/races/R001", cut.Find("fluent-link[href='/races/R001']").GetAttribute("href"));
        });
        Assert.AreEqual(1, handler.SearchCount);
    }

    [TestMethod]
    public async Task RaceList_NextPage_RequestsNextApiPage()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);
        var cut = context.Render<Races>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "1 / 2 ページ"));

        var next = cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("次へ"));
        await cut.InvokeAsync(() => next.Instance.OnClick.InvokeAsync(new MouseEventArgs()));

        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual(2, handler.LastRequestPage);
            StringAssert.Contains(cut.Markup, "2 / 2 ページ");
        });
    }

    [TestMethod]
    public void RaceDetail_RendersEntriesAndRelatedLinks()
    {
        using var handler = new RaceApiHandler { Detail = CreateRace() };
        using var context = CreateContext(handler);

        var cut = context.Render<RaceDetail>(parameters => parameters.Add(x => x.RaceId, "R001"));

        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "テストホース");
            Assert.AreEqual("/horses/H001", cut.Find("fluent-link[href='/horses/H001']").GetAttribute("href"));
            Assert.AreEqual("/jockeys/J001", cut.Find("fluent-link[href='/jockeys/J001']").GetAttribute("href"));
            StringAssert.Contains(cut.Markup, "1着");
        });
        Assert.AreEqual("/api/races/R001", handler.LastRequestPath);
    }

    [TestMethod]
    public void RaceList_WhenApiFails_ShowsErrorState()
    {
        using var handler = new RaceApiHandler { StatusCode = HttpStatusCode.ServiceUnavailable };
        using var context = CreateContext(handler);

        var cut = context.Render<Races>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "レース一覧を取得できませんでした。"));
    }

    [TestMethod]
    public async Task RaceList_Search_UsesFiltersAndSynchronizesUrl()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);
        var cut = context.Render<Races>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "テストレース"));

        await cut.Find("fluent-text-input[placeholder='レース名']").InputAsync("有馬記念");
        var search = cut.FindComponents<FluentButton>().Single(x => x.Markup.Contains("検索"));
        await cut.InvokeAsync(() => search.Instance.OnClick.InvokeAsync(new MouseEventArgs()));

        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual("有馬記念", handler.LastRequestRaceName);
            StringAssert.Contains(context.Services.GetRequiredService<NavigationManager>().Uri, "name=%E6%9C%89%E9%A6%AC%E8%A8%98%E5%BF%B5");
        });
    }

    [TestMethod]
    public async Task RaceList_NameInput_SearchesAfterShortPause()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);
        var cut = context.Render<Races>();
        cut.WaitForAssertion(() => Assert.AreEqual(1, handler.SearchCount));

        await cut.Find("fluent-text-input[placeholder='レース名']").InputAsync("有馬記念");
        Assert.AreEqual(1, handler.SearchCount);

        await Task.Delay(450);
        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual(2, handler.SearchCount);
            Assert.AreEqual("有馬記念", handler.LastRequestRaceName);
            Assert.AreEqual("有馬記念", QueryValue(context, "name"));
        });
    }

    [TestMethod]
    public async Task RaceList_CourseSelection_UpdatesUrlAndSearchesImmediately()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);
        var cut = context.Render<Races>();
        cut.WaitForAssertion(() => Assert.AreEqual(1, handler.SearchCount));

        var courseFilter = cut.FindAll("fluent-button[aria-haspopup='listbox']")
            .Single(button => button.GetAttribute("aria-label")!.StartsWith("開催場:", StringComparison.Ordinal));
        await courseFilter.ClickAsync();
        var courseList = cut.FindComponents<FluentListbox<FilterSelectionOption, string>>().First();
        await cut.InvokeAsync(() => courseList.Instance.ValueChanged.InvokeAsync("東京"));

        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual(2, handler.SearchCount);
            Assert.AreEqual("東京", handler.LastRequestCourseCode);
            Assert.AreEqual("東京", QueryValue(context, "course"));
        });
    }

    [TestMethod]
    public async Task RaceList_BrowserHistoryChange_RestoresFiltersAndSearches()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/races?course=東京&from=2026-10-01");
        var cut = context.Render<Races>();
        cut.WaitForAssertion(() => Assert.AreEqual("東京", cut.FindComponent<RaceSearchForm>().Instance.Course));

        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/races?course=中山&to=2026-10-05");

        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual("中山", cut.FindComponent<RaceSearchForm>().Instance.Course);
            Assert.IsNull(cut.FindComponent<RaceSearchForm>().Instance.From);
            Assert.AreEqual(new DateOnly(2026, 10, 5), cut.FindComponent<RaceSearchForm>().Instance.To);
            Assert.AreEqual("中山", handler.LastRequestCourseCode);
            Assert.AreEqual("2026-10-05", handler.LastRequestDateTo);
        });
    }

    [TestMethod]
    public async Task RaceList_LateOldResponse_DoesNotReplaceLatestSearchResults()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);
        var cut = context.Render<Races>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "テストレース"));

        var oldRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.SearchRequestHandler = async (request, _) =>
        {
            var name = RaceApiHandler.GetQuery(request, "raceName");
            if (name == "先行検索")
            {
                oldRequestStarted.SetResult();
                await releaseOldRequest.Task;
                return SearchResponse("古い検索結果");
            }

            return SearchResponse("最新の検索結果");
        };

        var nameInput = cut.Find("fluent-text-input[placeholder='レース名']");
        await nameInput.InputAsync("先行検索");
        var search = cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("検索"));
        var oldSearchTask = cut.InvokeAsync(() => search.Instance.OnClick.InvokeAsync(new MouseEventArgs()));
        await oldRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await cut.Find("fluent-text-input[placeholder='レース名']").InputAsync("最新検索");
        search = cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("検索"));
        await cut.InvokeAsync(() => search.Instance.OnClick.InvokeAsync(new MouseEventArgs()));
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "最新の検索結果"));

        releaseOldRequest.SetResult();
        await oldSearchTask;
        await Task.Delay(50);
        StringAssert.Contains(cut.Markup, "最新の検索結果");
        Assert.IsFalse(cut.Markup.Contains("古い検索結果", StringComparison.Ordinal));
    }

    private static HttpResponseMessage SearchResponse(string raceName)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SearchRacesResponse(
                [new("R002", new DateOnly(2026, 10, 3), "中山", 11, raceName, RaceStatus.ResultDeclared, 1, "テストホース", DateTimeOffset.UtcNow)],
                new PaginationDto(1, 1, 1, 1)))
        };

    [TestMethod]
    public void RaceList_DefaultsToTodayAndKeepsPeriodRelativeInUrl()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);

        var cut = context.Render<Races>();

        cut.WaitForAssertion(() => Assert.AreEqual("今日", SelectedPeriodTab(cut)));
        Assert.AreEqual("today", QueryValue(context, "period"));
        Assert.IsNull(QueryValue(context, "from"));
        Assert.IsNull(QueryValue(context, "to"));
        Assert.AreEqual("2026-10-07", handler.LastRequestDateFrom);
        Assert.AreEqual("2026-10-07", handler.LastRequestDateTo);
    }

    [TestMethod]
    public async Task RaceList_PeriodTabs_ApplyTodayWeekMonthAndAllPeriods()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);
        var drawerHost = context.Render<FluentDialogProvider>();
        var cut = context.Render<Races>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "テストレース"));

        Assert.AreEqual("今日", SelectedPeriodTab(cut));
        Assert.AreEqual("2026-10-07", handler.LastRequestDateFrom);
        Assert.AreEqual("2026-10-07", handler.LastRequestDateTo);
        Assert.AreEqual("today", QueryValue(context, "period"));
        Assert.IsNull(QueryValue(context, "from"));
        Assert.IsNull(QueryValue(context, "to"));
        Assert.AreEqual("今日", SelectedPeriodTab(cut));

        await SelectPeriodTabAsync(cut, "今週");
        Assert.AreEqual("2026-10-01", handler.LastRequestDateFrom);
        Assert.AreEqual("2026-10-07", handler.LastRequestDateTo);
        Assert.AreEqual("week", QueryValue(context, "period"));
        Assert.IsNull(QueryValue(context, "from"));
        Assert.IsNull(QueryValue(context, "to"));
        Assert.AreEqual("今週", SelectedPeriodTab(cut));
        Assert.AreEqual(0, cut.FindComponents<FluentDatePicker<DateOnly?>>().Count);
        Assert.AreEqual(0, drawerHost.FindAll("fluent-drawer").Count);

        await SelectPeriodTabAsync(cut, "過去一ヶ月");
        Assert.AreEqual("2026-09-07", handler.LastRequestDateFrom);
        Assert.AreEqual("2026-10-07", handler.LastRequestDateTo);
        Assert.AreEqual("past-month", QueryValue(context, "period"));

        await SelectPeriodTabAsync(cut, "すべての期間");
        Assert.IsNull(handler.LastRequestDateFrom);
        Assert.IsNull(handler.LastRequestDateTo);
        Assert.AreEqual("all", QueryValue(context, "period"));
    }

    [TestMethod]
    public void RaceList_PeriodUrl_RestoresTabAndResolvedDates()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);
        context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/races?period=week&from=2026-09-24&to=2026-09-30");

        var cut = context.Render<Races>();

        cut.WaitForAssertion(() => Assert.AreEqual("今週", SelectedPeriodTab(cut)));
        Assert.AreEqual("2026-09-24", handler.LastRequestDateFrom);
        Assert.AreEqual("2026-09-30", handler.LastRequestDateTo);
        Assert.AreEqual("week", QueryValue(context, "period"));
    }

    [TestMethod]
    public void RaceList_PeriodUrl_ResolvesMissingDatesAndKeepsPeriodRelative()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);
        context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/races?period=past-month");

        var cut = context.Render<Races>();

        cut.WaitForAssertion(() => Assert.AreEqual("過去一ヶ月", SelectedPeriodTab(cut)));
        Assert.AreEqual("2026-09-07", handler.LastRequestDateFrom);
        Assert.AreEqual("2026-10-07", handler.LastRequestDateTo);
        Assert.AreEqual("past-month", QueryValue(context, "period"));
        Assert.IsNull(QueryValue(context, "from"));
        Assert.IsNull(QueryValue(context, "to"));
    }

    [TestMethod]
    public async Task RaceList_ManualDateChange_MovesTabToAllAndKeepsResolvedFiltersInUrl()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);
        var drawerHost = context.Render<FluentDialogProvider>();
        context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/races?period=week&from=2026-10-01&to=2026-10-07");
        var cut = context.Render<Races>();
        cut.WaitForAssertion(() => Assert.AreEqual("今週", SelectedPeriodTab(cut)));

        var dateFilterTrigger = cut.FindAll("fluent-button[aria-haspopup='listbox']")
            .Single(button => button.GetAttribute("aria-label")!.StartsWith("開催日:", StringComparison.Ordinal));
        await dateFilterTrigger.ClickAsync();
        var dateFilterOptions = cut.FindComponents<FluentListbox<FilterSelectionOption, string>>().Last();
        var openingDrawer = cut.InvokeAsync(() => dateFilterOptions.Instance.ValueChanged.InvokeAsync("custom"));
        drawerHost.WaitForAssertion(() => Assert.AreEqual(2, drawerHost.FindComponents<FluentDatePicker<DateOnly?>>().Count));
        var startDate = drawerHost.FindComponents<FluentDatePicker<DateOnly?>>().First();
        await drawerHost.InvokeAsync(() => startDate.Instance.ValueChanged.InvokeAsync(new DateOnly(2026, 10, 2)));
        Assert.AreEqual(new DateOnly(2026, 10, 2), drawerHost.FindComponents<FluentDatePicker<DateOnly?>>().First().Instance.Value);
        var endDate = drawerHost.FindComponents<FluentDatePicker<DateOnly?>>().Last();
        await drawerHost.InvokeAsync(() => endDate.Instance.ValueChanged.InvokeAsync(new DateOnly(2026, 10, 7)));
        var applyDateRange = drawerHost.FindComponents<FluentButton>().Single(button => button.Markup.Contains("適用"));
        await drawerHost.InvokeAsync(() => applyDateRange.Instance.OnClick.InvokeAsync());
        await openingDrawer;

        Assert.AreEqual(new DateOnly(2026, 10, 2), cut.FindComponent<RaceSearchForm>().Instance.From);
        Assert.AreEqual("すべての期間", SelectedPeriodTab(cut));
        var search = cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("検索"));
        await cut.InvokeAsync(() => search.Instance.OnClick.InvokeAsync(new MouseEventArgs()));

        Assert.AreEqual("all", QueryValue(context, "period"));
        Assert.IsNull(QueryValue(context, "dateRange"));
        Assert.AreEqual("2026-10-02", QueryValue(context, "from"));
        Assert.AreEqual("2026-10-07", QueryValue(context, "to"));
        Assert.AreEqual("2026-10-02", handler.LastRequestDateFrom);
        Assert.AreEqual("2026-10-07", handler.LastRequestDateTo);
    }

    [TestMethod]
    public async Task RaceList_CustomDateRange_UpdatesBadgeAndSearchesImmediately()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);
        var drawerHost = context.Render<FluentDialogProvider>();
        var cut = context.Render<Races>();
        cut.WaitForAssertion(() => Assert.AreEqual(1, handler.SearchCount));

        var trigger = cut.FindAll("fluent-button[aria-haspopup='listbox']")
            .Single(button => button.GetAttribute("aria-label")!.StartsWith("開催日:", StringComparison.Ordinal));
        await trigger.ClickAsync();
        var listbox = cut.FindComponents<FluentListbox<FilterSelectionOption, string>>().Last();
        var openingDrawer = cut.InvokeAsync(() => listbox.Instance.ValueChanged.InvokeAsync("custom"));
        drawerHost.WaitForAssertion(() => Assert.AreEqual(2, drawerHost.FindComponents<FluentDatePicker<DateOnly?>>().Count));
        await drawerHost.InvokeAsync(() => drawerHost.FindComponents<FluentDatePicker<DateOnly?>>().First().Instance.ValueChanged.InvokeAsync(new DateOnly(2026, 10, 1)));
        await drawerHost.InvokeAsync(() => drawerHost.FindComponents<FluentDatePicker<DateOnly?>>().Last().Instance.ValueChanged.InvokeAsync(new DateOnly(2026, 10, 10)));
        var apply = drawerHost.FindComponents<FluentButton>().Single(button => button.Markup.Contains("適用"));

        await drawerHost.InvokeAsync(() => apply.Instance.OnClick.InvokeAsync());
        await openingDrawer;

        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual(2, handler.SearchCount);
            Assert.AreEqual("2026-10-01", handler.LastRequestDateFrom);
            Assert.AreEqual("2026-10-10", handler.LastRequestDateTo);
            StringAssert.Contains(cut.Find("fluent-button[aria-haspopup='listbox'][aria-label^='開催日']").TextContent, "2026/10/01 - 2026/10/10");
            Assert.AreEqual("すべての期間", SelectedPeriodTab(cut));
        });
    }

    private static string SelectedPeriodTab(IRenderedComponent<Races> cut)
        => cut.FindComponent<FluentTabs>().Instance.ActiveTabId switch
        {
            "today" => "今日",
            "week" => "今週",
            "past-month" => "過去一ヶ月",
            "all" => "すべての期間",
            _ => throw new AssertFailedException("選択中の期間タブが不明です。")
        };

    private static Task SelectPeriodTabAsync(IRenderedComponent<Races> cut, string text)
    {
        var tabId = text switch
        {
            "今日" => "today",
            "今週" => "week",
            "過去一ヶ月" => "past-month",
            "すべての期間" => "all",
            _ => throw new ArgumentOutOfRangeException(nameof(text), text, "未知の期間タブです。")
        };

        var tabs = cut.FindComponent<FluentTabs>().Instance;
        return cut.InvokeAsync(() => tabs.ActiveTabIdChanged.InvokeAsync(tabId));
    }

    private static string? QueryValue(BunitContext context, string key)
    {
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(navigation.ToAbsoluteUri(navigation.Uri).Query);
        return query.TryGetValue(key, out var value) ? value.ToString() : null;
    }

    private static BunitContext CreateContext(RaceApiHandler handler)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton<TimeProvider>(new FrozenTimeProvider(new DateTimeOffset(2026, 10, 7, 2, 0, 0, TimeSpan.Zero)));
        context.Services.AddHorseRacingApiClient(options =>
        {
            options.BaseAddress = new Uri("http://localhost");
            options.ApiKey = "test-key";
        }).ConfigurePrimaryHttpMessageHandler(() => handler);
        context.Services.AddScoped(sp => sp.GetRequiredService<IApiClientFactory>().Create<IRacesApi>());
        return context;
    }

    private static RaceDto CreateRace() => new(
        "R001", new DateOnly(2026, 10, 3), "中山", 11, "テストレース", RaceStatus.ResultDeclared,
        1, 1, "G1", "芝", 2000, "右", 1,
        [new("E001", "H001", "テストホース", 1, "J001", "テスト騎手", "T001", "テスト調教師", 1, null, "牡", 3, null, null, null)],
        [], [], null, "テストホース", "H001", null, DateTimeOffset.UtcNow, [new("E001", "H001", "テストホース", 1, 1, "1:58.0", null, null, null, null, null)], null);

    private sealed class RaceApiHandler : HttpMessageHandler
    {
        public SearchRacesResponse? Search { get; set; }
        public RaceDto? Detail { get; set; }
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public int SearchCount { get; private set; }
        public string? LastRequestPath { get; private set; }
        public int LastRequestPage { get; private set; }
        public string? LastRequestRaceName { get; private set; }
        public string? LastRequestCourseCode { get; private set; }
        public string? LastRequestDateFrom { get; private set; }
        public string? LastRequestDateTo { get; private set; }
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? SearchRequestHandler { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestPath = request.RequestUri?.PathAndQuery;
            if (request.RequestUri?.AbsolutePath == "/api/races" && request.Method == HttpMethod.Get)
            {
                SearchCount++;
                LastRequestPage = int.TryParse(GetQuery(request, "page"), out var page) ? page : 1;
                LastRequestRaceName = GetQuery(request, "raceName");
                LastRequestCourseCode = GetQuery(request, "racecourseCode");
                LastRequestDateFrom = GetQuery(request, "raceDateFrom");
                LastRequestDateTo = GetQuery(request, "raceDateTo");
                if (SearchRequestHandler is not null)
                {
                    return await SearchRequestHandler(request, cancellationToken);
                }

                var response = Search ?? new SearchRacesResponse(
                    [new("R001", new DateOnly(2026, 10, 3), "中山", 11, "テストレース", RaceStatus.ResultDeclared, 1, "テストホース", DateTimeOffset.UtcNow)],
                    new PaginationDto(LastRequestPage, 1, 2, 2));
                return CreateResponse(response);
            }
            if (request.RequestUri?.AbsolutePath == "/api/races/R001" && request.Method == HttpMethod.Get)
            {
                return CreateResponse(new GetRaceResponse(Detail ?? CreateRace()));
            }
            return new HttpResponseMessage(StatusCode == HttpStatusCode.OK ? HttpStatusCode.NotFound : StatusCode)
            {
                RequestMessage = request
            };
        }

        private HttpResponseMessage CreateResponse<T>(T value)
            => new(StatusCode) { Content = JsonContent.Create(value), RequestMessage = new HttpRequestMessage() };

        public static string? GetQuery(HttpRequestMessage request, string key)
        {
            var query = request.RequestUri?.Query.TrimStart('?') ?? string.Empty;
            return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .Select(parts => new { Key = Uri.UnescapeDataString(parts[0]), Value = Uri.UnescapeDataString(parts[1].Replace('+', ' ')) })
                .FirstOrDefault(pair => pair.Key == key)?.Value;
        }
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
