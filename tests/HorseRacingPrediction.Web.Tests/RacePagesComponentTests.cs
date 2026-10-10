using System.Net;
using System.Net.Http.Json;
using Bunit;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.ApiClient.Races;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Races;
using HorseRacingPrediction.Web.Components.Pages.Races;
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
            Assert.AreEqual("/races/R001", cut.Find("a[href='/races/R001']").GetAttribute("href"));
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
            Assert.AreEqual("/horses/H001", cut.Find("a[href='/horses/H001']").GetAttribute("href"));
            Assert.AreEqual("/jockeys/J001", cut.Find("a[href='/jockeys/J001']").GetAttribute("href"));
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
    public async Task RaceList_PeriodTabs_ApplyTodayWeekMonthAndAllPeriods()
    {
        using var handler = new RaceApiHandler();
        using var context = CreateContext(handler);
        var cut = context.Render<Races>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "テストレース"));

        await cut.Find("nav[aria-label='開催期間'] button").ClickAsync();
        Assert.AreEqual("2026-10-07", handler.LastRequestDateFrom);
        Assert.AreEqual("2026-10-07", handler.LastRequestDateTo);
        Assert.IsTrue(cut.Markup.Contains("今日</button>", StringComparison.Ordinal));

        await FindPeriodTab(cut, "今週").ClickAsync();
        Assert.AreEqual("2026-10-01", handler.LastRequestDateFrom);
        Assert.AreEqual("2026-10-07", handler.LastRequestDateTo);

        await FindPeriodTab(cut, "今月").ClickAsync();
        Assert.AreEqual("2026-10-01", handler.LastRequestDateFrom);
        Assert.AreEqual("2026-10-31", handler.LastRequestDateTo);

        await FindPeriodTab(cut, "すべての期間").ClickAsync();
        Assert.IsNull(handler.LastRequestDateFrom);
        Assert.IsNull(handler.LastRequestDateTo);
    }

    private static AngleSharp.Dom.IElement FindPeriodTab(IRenderedComponent<Races> cut, string text)
        => cut.FindAll("nav[aria-label='開催期間'] button").Single(button => button.TextContent.Trim() == text);

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
        public string? LastRequestDateFrom { get; private set; }
        public string? LastRequestDateTo { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestPath = request.RequestUri?.PathAndQuery;
            if (request.RequestUri?.AbsolutePath == "/api/races" && request.Method == HttpMethod.Get)
            {
                SearchCount++;
                LastRequestPage = int.TryParse(GetQuery(request, "page"), out var page) ? page : 1;
                LastRequestRaceName = GetQuery(request, "raceName");
                LastRequestDateFrom = GetQuery(request, "raceDateFrom");
                LastRequestDateTo = GetQuery(request, "raceDateTo");
                var response = Search ?? new SearchRacesResponse(
                    [new("R001", new DateOnly(2026, 10, 3), "中山", 11, "テストレース", RaceStatus.ResultDeclared, 1, "テストホース", DateTimeOffset.UtcNow)],
                    new PaginationDto(LastRequestPage, 1, 2, 2));
                return Task.FromResult(CreateResponse(response));
            }
            if (request.RequestUri?.AbsolutePath == "/api/races/R001" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(CreateResponse(new GetRaceResponse(Detail ?? CreateRace())));
            }
            return Task.FromResult(new HttpResponseMessage(StatusCode == HttpStatusCode.OK ? HttpStatusCode.NotFound : StatusCode)
            {
                RequestMessage = request
            });
        }

        private HttpResponseMessage CreateResponse<T>(T value)
            => new(StatusCode) { Content = JsonContent.Create(value), RequestMessage = new HttpRequestMessage() };

        private static string? GetQuery(HttpRequestMessage request, string key)
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
