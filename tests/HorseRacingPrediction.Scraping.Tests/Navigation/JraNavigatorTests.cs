using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Navigation;

[TestClass]
public sealed class JraNavigatorTests
{
    private const string KeibaTopUrl = "https://www.jra.go.jp/keiba/";
    private const string CalendarUrl = "https://www.jra.go.jp/keiba/calendar/";

    private static PageSnapshot BuildCalendarSnapshot(
        string url,
        IEnumerable<PageLinkSnapshot> links)
    {
        var table = new PageTableSnapshot(
            Headers: [],
            Rows:
            [
                ["5 中山", "6", "7"],
            ]);

        var section = new PageSectionSnapshot(
            title: "開催日程",
            mainText: string.Empty,
            links: links.ToList(),
            actions: [],
            tables: [table],
            headings: ["開催日程>2026年9月"]);

        return new PageSnapshot(url, "開催日程", [section]);
    }

    private static PageSnapshot BuildRaceListSnapshot(string url)
    {
        var table = new PageTableSnapshot(
            Headers: ["R", "発走時刻", "レース名"],
            Rows: [["11R", "15:40", "テストステークス"]]);

        var section = new PageSectionSnapshot(
            title: "レース一覧",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山"]);

        return new PageSnapshot(url, "2026年9月5日 中山 レース一覧", [section]);
    }

    private static PageSnapshot BuildRaceCardSnapshot(string url, string headingSuffix = "11R")
    {
        var table = new PageTableSnapshot(
            Headers: ["馬番", "馬名", "騎手"],
            Rows: [["1", "テストホース", "テスト騎手"]]);

        var section = new PageSectionSnapshot(
            title: "出馬表",
            mainText: string.Empty,
            links: [],
            actions: [],
            tables: [table],
            headings: [$"2026年9月5日 中山 {headingSuffix}"]);

        return new PageSnapshot(url, $"2026年9月5日 中山 {headingSuffix} 出馬表", [section]);
    }

    private static JraPageReader CreateReader(FakeWebBrowser browser)
        => new(browser, [new CalendarPageParser(), new RaceListPageParser(), new RaceCardPageParser()]);

    [TestMethod]
    public async Task ToKeibaTopAsync_NavigatesToKeibaTopUrl()
    {
        var browser = new FakeWebBrowser();
        var navigator = new JraNavigator(browser, CreateReader(browser));

        await navigator.ToKeibaTopAsync();

        CollectionAssert.Contains(browser.NavigatedUrls, KeibaTopUrl);
    }

    [TestMethod]
    public async Task ToCalendarAsync_LinkFound_ResolvesRelativeUrlAndNavigates()
    {
        var browser = new FakeWebBrowser();
        browser.SetCurrentUrl(KeibaTopUrl);
        browser.SetLinks(KeibaTopUrl,
        [
            new PageLinkSnapshot("calendar/", "開催日程"),
        ]);
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        var page = await navigator.ToCalendarAsync(new YearMonth(2026, 9));

        CollectionAssert.Contains(browser.NavigatedUrls, CalendarUrl);
        Assert.AreEqual(JraPageKind.Calendar, page.Kind);
    }

    [TestMethod]
    public async Task ToCalendarAsync_LinkNotFound_FallsBackToDirectUrl()
    {
        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        await navigator.ToCalendarAsync(new YearMonth(2026, 9));

        CollectionAssert.Contains(browser.NavigatedUrls, CalendarUrl);
    }

    [TestMethod]
    public async Task ToRaceListAsync_LinkFound_NavigatesAndReturnsRaceListPage()
    {
        const string raceListUrl = "https://www.jra.go.jp/keiba/sample/racelist/";

        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));
        browser.SetLinks(
            CalendarUrl,
            [new PageLinkSnapshot(raceListUrl, "9月5日 中山")]);
        browser.SetSnapshot(raceListUrl, BuildRaceListSnapshot(raceListUrl));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        var page = await navigator.ToRaceListAsync(
            new DateOnly(2026, 9, 5),
            RaceCourse.Nakayama);

        Assert.AreEqual(JraPageKind.RaceList, page.Kind);
        var raceList = (JraRaceListPage)page;
        Assert.AreEqual(11, raceList.Races[0].Number);
    }

    [TestMethod]
    public async Task ToRaceListAsync_DateNotInCalendar_Throws()
    {
        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => navigator.ToRaceListAsync(
                new DateOnly(2026, 9, 6),
                RaceCourse.Nakayama));
    }

    [TestMethod]
    public async Task ToRaceListAsync_CourseNotOnDate_Throws()
    {
        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => navigator.ToRaceListAsync(
                new DateOnly(2026, 9, 5),
                RaceCourse.Hanshin));
    }

    [TestMethod]
    public async Task ToRaceListAsync_LinkNotFound_Throws()
    {
        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => navigator.ToRaceListAsync(
                new DateOnly(2026, 9, 5),
                RaceCourse.Nakayama));
    }

    private static PageSnapshot BuildRaceListSnapshotWithCardLink(string url, string raceCardUrl)
    {
        var table = new PageTableSnapshot(
            Headers: ["R", "発走時刻", "レース名"],
            Rows: [["11R", "15:40", "テストステークス"]]);

        var section = new PageSectionSnapshot(
            title: "レース一覧",
            mainText: string.Empty,
            links: [new PageLinkSnapshot(raceCardUrl, "11R 出馬表")],
            actions: [],
            tables: [table],
            headings: ["2026年9月5日 中山"]);

        return new PageSnapshot(url, "2026年9月5日 中山 レース一覧", [section]);
    }

    [TestMethod]
    public async Task ToRaceCardAsync_UsesRaceListLinkToReachRaceCardPage()
    {
        const string raceListUrl = "https://www.jra.go.jp/keiba/sample/racelist/";
        const string raceCardUrl = "https://www.jra.go.jp/keiba/sample/racecard/11/";

        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));
        browser.SetLinks(
            CalendarUrl,
            [new PageLinkSnapshot(raceListUrl, "9月5日 中山")]);
        browser.SetSnapshot(raceListUrl, BuildRaceListSnapshot(raceListUrl));
        browser.SetLinks(
            raceListUrl,
            [new PageLinkSnapshot(raceCardUrl, "11R 出馬表")]);
        browser.SetSnapshot(raceCardUrl, BuildRaceCardSnapshot(raceCardUrl));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        var raceId = new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 11);

        var page = await navigator.ToRaceCardAsync(raceId);

        Assert.AreEqual(JraPageKind.RaceCard, page.Kind);
        CollectionAssert.Contains(browser.NavigatedUrls, raceCardUrl);
    }

    [TestMethod]
    public async Task ToRaceCardAsync_RaceNotInList_Throws()
    {
        const string raceListUrl = "https://www.jra.go.jp/keiba/sample/racelist/";

        var browser = new FakeWebBrowser();
        browser.SetSnapshot(CalendarUrl, BuildCalendarSnapshot(CalendarUrl, []));
        browser.SetLinks(
            CalendarUrl,
            [new PageLinkSnapshot(raceListUrl, "9月5日 中山")]);
        browser.SetSnapshot(raceListUrl, BuildRaceListSnapshot(raceListUrl));

        var navigator = new JraNavigator(browser, CreateReader(browser));

        var raceId = new RaceId(new DateOnly(2026, 9, 5), RaceCourse.Nakayama, 5);

        await Assert.ThrowsExactlyAsync<JraNavigationException>(
            () => navigator.ToRaceCardAsync(raceId));
    }
}
